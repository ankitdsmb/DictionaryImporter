using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;

namespace DictionaryImporter.Sources.Collins;

public sealed class CollinsExtractor : IDataExtractor<CollinsRawEntry>
{
    private const string SourceCode = "ENG_COLLINS";

    public async IAsyncEnumerable<CollinsRawEntry> ExtractAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        CollinsRawEntry? currentEntry = null;
        var entryContent = new List<string>();

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                if (entryContent.Count > 0)
                    entryContent.Add(line);
                continue;
            }

            line = line.Trim();

            // Check for entry separator
            if (IsEntrySeparator(line))
            {
                // Process the current entry if exists
                if (currentEntry != null && entryContent.Count > 0)
                {
                    ProcessEntryContent(currentEntry, entryContent);

                    // ✅ STRICT STOP
                    if (!Helper.ShouldContinueProcessing(SourceCode, null))
                        yield break;

                    yield return currentEntry;
                }

                // Reset for next entry
                currentEntry = null;
                entryContent.Clear();
                continue;
            }

            // Try to parse headword
            if (TryParseHeadword(line, out var headword))
            {
                // Process previous entry if exists
                if (currentEntry != null && entryContent.Count > 0)
                {
                    ProcessEntryContent(currentEntry, entryContent);

                    // ✅ STRICT STOP
                    if (!Helper.ShouldContinueProcessing(SourceCode, null))
                        yield break;

                    yield return currentEntry;
                }

                // Start new entry
                currentEntry = new CollinsRawEntry { Headword = headword };
                entryContent.Clear();
                continue;
            }

            // Add content to current entry
            if (currentEntry != null)
            {
                entryContent.Add(line);
            }
        }

        // Process the last entry
        if (currentEntry != null && entryContent.Count > 0)
        {
            ProcessEntryContent(currentEntry, entryContent);

            // ✅ STRICT STOP
            if (!Helper.ShouldContinueProcessing(SourceCode, null))
                yield break;

            yield return currentEntry;
        }
    }

    private void ProcessEntryContent(CollinsRawEntry entry, List<string> content)
    {
        var currentSense = new CollinsSenseRaw();
        var collectingExamples = false;
        var currentDefinition = new List<string>();
        var currentExamples = new List<string>();
        var noteBuffer = new List<string>();

        for (int i = 0; i < content.Count; i++)
        {
            var line = content[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                if (collectingExamples && currentExamples.Count > 0)
                {
                    currentSense.Examples.AddRange(currentExamples);
                    currentExamples.Clear();
                }
                continue;
            }

            // Check if this line starts a new sense
            if (IsNewSenseStart(line, out var senseNumber, out var pos))
            {
                // Save previous sense if it has content
                if (currentSense.SenseNumber > 0 || currentDefinition.Count > 0)
                {
                    if (currentDefinition.Count > 0)
                    {
                        currentSense.Definition = string.Join(" ", currentDefinition).Trim();
                        currentDefinition.Clear();
                    }
                    if (currentExamples.Count > 0)
                    {
                        currentSense.Examples.AddRange(currentExamples);
                        currentExamples.Clear();
                    }
                    if (!string.IsNullOrWhiteSpace(currentSense.Definition) || currentSense.Examples.Count > 0)
                    {
                        entry.Senses.Add(currentSense);
                    }
                }

                // Start new sense
                currentSense = new CollinsSenseRaw
                {
                    SenseNumber = senseNumber,
                    PartOfSpeech = pos
                };
                collectingExamples = false;
                currentDefinition.Clear();
                currentExamples.Clear();
                noteBuffer.Clear();

                // Extract the English part after POS
                var englishPart = ExtractEnglishDefinitionFromSenseLine(line);
                if (!string.IsNullOrWhiteSpace(englishPart))
                {
                    currentDefinition.Add(englishPart);
                }
                continue;
            }

            // Check for usage notes/labels (【搭配模式】, 【语法信息】, etc.)
            if (line.StartsWith("【"))
            {
                ProcessNoteLine(line, currentSense);
                continue;
            }

            // Check for examples
            if (line.StartsWith("..."))
            {
                collectingExamples = true;
                var example = CleanExample(line.Substring(3));
                if (!string.IsNullOrWhiteSpace(example))
                {
                    currentExamples.Add(example);
                }
                continue;
            }
            else if (collectingExamples && IsExampleLine(line))
            {
                var example = CleanExample(line);
                if (!string.IsNullOrWhiteSpace(example))
                {
                    currentExamples.Add(example);
                }
                continue;
            }

            // If we're not collecting examples and it's not a note, it's part of the definition
            if (!collectingExamples)
            {
                var cleanedLine = CleanDefinitionLine(line);
                if (!string.IsNullOrWhiteSpace(cleanedLine))
                {
                    currentDefinition.Add(cleanedLine);
                }
            }
        }

        // Add the last sense
        if (currentSense.SenseNumber > 0 || currentDefinition.Count > 0)
        {
            if (currentDefinition.Count > 0)
            {
                currentSense.Definition = string.Join(" ", currentDefinition).Trim();
            }
            if (currentExamples.Count > 0)
            {
                currentSense.Examples.AddRange(currentExamples);
            }
            if (!string.IsNullOrWhiteSpace(currentSense.Definition) || currentSense.Examples.Count > 0)
            {
                entry.Senses.Add(currentSense);
            }
        }
    }

    private bool IsNewSenseStart(string line, out int senseNumber, out string partOfSpeech)
    {
        senseNumber = 0;
        partOfSpeech = "unk";

        // Pattern: 1.N-VAR, 2.VERB, 3.ADJ, etc.
        var match = Regex.Match(line, @"^(?<num>\d+)\.(?<pos>[A-Z][A-Z\-]+)");
        if (match.Success)
        {
            senseNumber = int.Parse(match.Groups["num"].Value);
            partOfSpeech = NormalizePos(match.Groups["pos"].Value);
            return true;
        }

        // Pattern: 1.   →see: ah;
        if (line.Contains("→see:"))
        {
            var numMatch = Regex.Match(line, @"^(?<num>\d+)\.");
            if (numMatch.Success)
            {
                senseNumber = int.Parse(numMatch.Groups["num"].Value);
                partOfSpeech = "ref";
                return true;
            }
        }

        return false;
    }

    private string ExtractEnglishDefinitionFromSenseLine(string line)
    {
        // Remove the sense header (e.g., "1.N-VAR    可变名词  喜好；偏好；偏爱")
        var match = Regex.Match(line, @"^\d+\.[A-Z][A-Z\-]+\s*");
        if (match.Success)
        {
            var afterHeader = line.Substring(match.Length);
            // Remove Chinese characters and clean up
            return CleanDefinitionLine(afterHeader);
        }

        // For cross-references: "1.   →see: ah;"
        if (line.Contains("→see:"))
        {
            return line; // Keep as-is for now
        }

        return CleanDefinitionLine(line);
    }

    private void ProcessNoteLine(string line, CollinsSenseRaw sense)
    {
        if (line.StartsWith("【搭配模式】"))
        {
            var match = Regex.Match(line, @"【搭配模式】：\s*(.+)");
            if (match.Success)
            {
                sense.GrammarInfo = CleanNote(match.Groups[1].Value);
            }
        }
        else if (line.StartsWith("【语法信息】"))
        {
            var match = Regex.Match(line, @"【语法信息】：\s*(.+)");
            if (match.Success)
            {
                sense.GrammarInfo = CleanNote(match.Groups[1].Value);
            }
        }
        else if (line.StartsWith("【语域标签】") || line.StartsWith("【FIELD标签】"))
        {
            var match = Regex.Match(line, @"【[^】]+】：\s*(.+)");
            if (match.Success)
            {
                sense.DomainLabel = ExtractDomainLabel(match.Groups[1].Value);
            }
        }
        else if (line.StartsWith("【") && line.EndsWith("】"))
        {
            // Generic note
            var note = line.Trim('【', '】');
            sense.UsageNote = CleanNote(note);
        }
    }

    private string CleanDefinitionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        // Remove Chinese characters
        var cleaned = RemoveChineseCharacters(line);

        // Remove extra formatting
        cleaned = cleaned.Replace(" ; ; ", " ")
                        .Replace(" ; ", " ")
                        .Replace("  ", " ")
                        .Trim();

        // Remove trailing Chinese punctuation that might remain
        cleaned = Regex.Replace(cleaned, @"[。，；：！？]+$", "");

        return cleaned.Trim();
    }

    private string CleanExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return example;

        // Remove Chinese characters
        var cleaned = RemoveChineseCharacters(example);

        // Clean up formatting
        cleaned = cleaned.Replace("  ", " ")
                        .Trim();

        // Ensure it ends with punctuation
        if (!string.IsNullOrEmpty(cleaned) &&
            !cleaned.EndsWith(".") &&
            !cleaned.EndsWith("!") &&
            !cleaned.EndsWith("?") &&
            !cleaned.EndsWith("..."))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    private string CleanNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return note;

        // Remove Chinese characters
        var cleaned = RemoveChineseCharacters(note);

        // Clean up
        cleaned = cleaned.Replace("  ", " ").Trim();

        return cleaned;
    }

    private string ExtractDomainLabel(string text)
    {
        // Extract English part (e.g., "BRIT 英" -> "BRIT")
        var match = Regex.Match(text, @"([A-Z]+)");
        return match.Success ? match.Groups[1].Value : text;
    }

    private bool IsExampleLine(string line)
    {
        // Check if line looks like an example (starts with capital, ends with punctuation)
        if (string.IsNullOrWhiteSpace(line) || line.Length < 10)
            return false;

        // Don't include lines that are clearly notes or definitions
        if (line.StartsWith("【") || line.Contains("→see:"))
            return false;

        // Check for proper English sentence structure
        return char.IsUpper(line[0]) &&
               (line.EndsWith(".") || line.EndsWith("!") || line.EndsWith("?") || line.EndsWith("..."));
    }

    // Helper methods
    private static bool IsEntrySeparator(string line)
    {
        return line.StartsWith("——————————————") ||
               line.StartsWith("---------------") ||
               line.StartsWith("================");
    }

    private static bool TryParseHeadword(string line, out string headword)
    {
        headword = string.Empty;

        // Pattern: ★☆☆   preference ●●○○○
        var match = Regex.Match(line, @"^[★☆●○\s]*(?<word>[a-zA-Z0-9\-\'\/ ]+?)(?:\s+[●○★☆]+)?$");
        if (match.Success)
        {
            headword = match.Groups["word"].Value.Trim();
            return true;
        }

        return false;
    }

    public static string RemoveChineseCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters
        var cleaned = Regex.Replace(text, @"[\u4E00-\u9FFF]+", " ");

        // Also remove Chinese punctuation
        cleaned = cleaned.Replace("。", ". ")
                        .Replace("，", ", ")
                        .Replace("；", "; ")
                        .Replace("：", ": ")
                        .Replace("！", "! ")
                        .Replace("？", "? ")
                        .Replace("（", " (")
                        .Replace("）", ") ")
                        .Replace("【", "[")
                        .Replace("】", "]")
                        .Replace("、", ", ")
                        .Replace("…", "... ")
                        .Replace("——", "-- ")
                        .Replace("～", "~ ");

        // Clean up multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    public static string NormalizePos(string pos)
    {
        if (string.IsNullOrWhiteSpace(pos))
            return "unk";

        return pos.ToUpper() switch
        {
            "N-COUNT" or "N-UNCOUNT" or "N-VAR" or "N-MASS" or "N-PLURAL" or "N-SING" => "noun",
            "VERB" or "V" or "V-ERG" or "V-PASSIVE" or "V-LINK" => "verb",
            "ADJ-GRADED" or "ADJ" or "ADJ CLASSIF" => "adj",
            "ADV" or "ADV-GRADED" => "adv",
            "PHRASE" or "PHR" => "phrase",
            "SUFFIX" => "suffix",
            "PREFIX" => "prefix",
            "PRON" => "pronoun",
            "PREP" => "preposition",
            "CONJ" => "conjunction",
            "INTERJ" => "interjection",
            "DET" or "DETERMINER" => "determiner",
            "MODAL" => "modal",
            "NUM" => "num",
            _ => pos.ToLower()
        };
    }
}