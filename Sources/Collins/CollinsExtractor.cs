using DictionaryImporter.Common;

namespace DictionaryImporter.Sources.Collins;

public sealed class CollinsExtractor : IDataExtractor<CollinsRawEntry>
{
    private const string SourceCode = "ENG_COLLINS";

    public async IAsyncEnumerable<CollinsRawEntry> ExtractAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        var currentEntry = new StringBuilder();
        var lineCount = 0;

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            ct.ThrowIfCancellationRequested();
            lineCount++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Check for entry separator
            if (IsEntrySeparator(line))
            {
                if (currentEntry.Length > 0)
                {
                    var rawEntry = ParseEntry(currentEntry.ToString());
                    if (rawEntry != null && rawEntry.Senses.Any())
                    {
                        if (!Helper.ShouldContinueProcessing(SourceCode, null))
                            yield break;

                        yield return rawEntry;
                    }
                    currentEntry.Clear();
                }
                continue;
            }

            currentEntry.AppendLine(line);
        }

        // Process the last entry
        if (currentEntry.Length > 0)
        {
            var rawEntry = ParseEntry(currentEntry.ToString());
            if (rawEntry != null && rawEntry.Senses.Any())
            {
                if (!Helper.ShouldContinueProcessing(SourceCode, null))
                    yield break;

                yield return rawEntry;
            }
        }
    }

    private CollinsRawEntry? ParseEntry(string entryText)
    {
        if (string.IsNullOrWhiteSpace(entryText))
            return null;

        var lines = entryText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return null;

        // Extract headword (first non-empty line without sense markers)
        string headword = string.Empty;
        for (int i = 0; i < Math.Min(3, lines.Count); i++)
        {
            if (IsHeadwordLine(lines[i]))
            {
                headword = ExtractHeadword(lines[i]);
                break;
            }
        }

        if (string.IsNullOrEmpty(headword))
            return null;

        var entry = new CollinsRawEntry { Headword = headword };
        var currentSense = new CollinsSenseRaw();
        var senseLines = new List<string>();
        bool collectingSense = false;
        int currentSenseNumber = 1;

        // Process all lines to find senses
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            // Check if this is a new sense (e.g., "1.PREFIX", "2.VERB", "3.DET")
            if (IsSenseStart(line, out var senseNumber, out var pos))
            {
                // Save previous sense if exists
                if (collectingSense && (currentSense.SenseNumber > 0 || senseLines.Count > 0))
                {
                    ProcessSenseContent(senseLines, currentSense);
                    entry.Senses.Add(currentSense);
                    senseLines.Clear();
                }

                // Start new sense
                currentSense = new CollinsSenseRaw
                {
                    SenseNumber = senseNumber,
                    PartOfSpeech = pos
                };
                senseLines.Clear();
                collectingSense = true;
                currentSenseNumber = senseNumber;

                // Add the sense header line
                senseLines.Add(line);
                continue;
            }

            // If we're in a multi-sense entry but haven't found sense yet, look for content
            if (!collectingSense && !IsHeadwordLine(line))
            {
                // Start collecting for first sense
                currentSense = new CollinsSenseRaw
                {
                    SenseNumber = currentSenseNumber,
                    PartOfSpeech = "unk"
                };
                collectingSense = true;
            }

            // Add line to current sense
            if (collectingSense)
            {
                senseLines.Add(line);
            }
        }

        // Process the last sense
        if (collectingSense && senseLines.Count > 0)
        {
            ProcessSenseContent(senseLines, currentSense);
            entry.Senses.Add(currentSense);
        }

        return entry.Senses.Any() ? entry : null;
    }

    private void ProcessSenseContent(List<string> senseLines, CollinsSenseRaw sense)
    {
        if (senseLines.Count == 0)
            return;

        var definitionParts = new List<string>();
        var examples = new List<string>();

        foreach (var line in senseLines)
        {
            // Skip if this is just the sense header (already processed)
            if (IsSenseStart(line, out _, out _))
                continue;

            // Check for examples - Lines starting with ... or •
            if (line.StartsWith("..."))
            {
                var example = ExtractEnglishExample(line.Substring(3));
                if (!string.IsNullOrWhiteSpace(example) && example.Length > 10)
                {
                    examples.Add(example);
                }
                continue;
            }

            if (line.StartsWith("•"))
            {
                var example = ExtractEnglishExample(line.Substring(1));
                if (!string.IsNullOrWhiteSpace(example) && example.Length > 10)
                {
                    examples.Add(example);
                }
                continue;
            }

            // Check for note/label lines
            if (line.StartsWith("【"))
            {
                ProcessNoteLine(line, sense);
                continue;
            }

            // Check for cross-references
            if (line.Contains("→see:"))
            {
                ProcessCrossReference(line, sense);
                continue;
            }

            // This is part of the definition
            var cleanedLine = CleanDefinitionLine(line);
            if (!string.IsNullOrWhiteSpace(cleanedLine))
            {
                definitionParts.Add(cleanedLine);
            }
        }

        // Build the definition
        if (definitionParts.Any())
        {
            sense.Definition = string.Join(" ", definitionParts).Trim();
            sense.Definition = CleanDefinitionText(sense.Definition);
        }

        // Store examples
        sense.Examples = examples;

        // Store raw text for parsing
        sense.RawText = string.Join("\n", senseLines);
    }

    private string ExtractEnglishExample(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        // Extract English part before Chinese characters
        var result = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c >= '\u4E00' && c <= '\u9FFF')
                break;
            result.Append(c);
        }

        var englishPart = result.ToString().Trim();

        // Clean up
        englishPart = englishPart.Replace("  ", " ").Trim();

        // Ensure proper ending
        if (!string.IsNullOrEmpty(englishPart) &&
            !englishPart.EndsWith(".") &&
            !englishPart.EndsWith("!") &&
            !englishPart.EndsWith("?"))
        {
            englishPart += ".";
        }

        return englishPart;
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
                if (string.IsNullOrEmpty(sense.GrammarInfo))
                    sense.GrammarInfo = CleanNote(match.Groups[1].Value);
                else
                    sense.GrammarInfo += "; " + CleanNote(match.Groups[1].Value);
            }
        }
        else if (line.StartsWith("【语域标签】"))
        {
            var match = Regex.Match(line, @"【语域标签】：\s*(.+)");
            if (match.Success)
            {
                sense.DomainLabel = ExtractDomainLabel(match.Groups[1].Value);
            }
        }
        else if (line.StartsWith("【FIELD标签】"))
        {
            var match = Regex.Match(line, @"【FIELD标签】：\s*(.+)");
            if (match.Success)
            {
                sense.DomainLabel = ExtractDomainLabel(match.Groups[1].Value);
            }
        }
        else if (line.StartsWith("【注意】"))
        {
            var match = Regex.Match(line, @"【注意】：\s*(.+)");
            if (match.Success)
            {
                sense.UsageNote = CleanNote(match.Groups[1].Value);
            }
        }
    }

    private void ProcessCrossReference(string line, CollinsSenseRaw sense)
    {
        var match = Regex.Match(line, @"→see:\s*([^;]+)");
        if (match.Success)
        {
            sense.CrossReference = match.Groups[1].Value;
        }
    }

    private string CleanDefinitionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        // Skip if it's an example or label
        if (line.StartsWith("...") || line.StartsWith("•") || line.StartsWith("【"))
            return string.Empty;

        // Extract English part before Chinese characters
        var result = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c >= '\u4E00' && c <= '\u9FFF')
                break;
            result.Append(c);
        }

        var cleaned = result.ToString();

        // Clean up formatting
        cleaned = cleaned.Replace(" ; ; ", " ")
                        .Replace(" ; ", " ")
                        .Replace("  ", " ")
                        .Trim();

        return cleaned;
    }

    private string CleanDefinitionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters
        var cleaned = RemoveChineseCharacters(text);

        // Clean up formatting
        cleaned = cleaned.Replace(" ; ; ", " ")
                        .Replace(" ; ", " ")
                        .Replace("  ", " ")
                        .Trim();

        // Ensure proper ending
        if (!string.IsNullOrEmpty(cleaned) &&
            !cleaned.EndsWith(".") &&
            !cleaned.EndsWith("!") &&
            !cleaned.EndsWith("?"))
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
        return cleaned.Replace("  ", " ").Trim();
    }

    private string ExtractDomainLabel(string text)
    {
        // Extract English labels like "BRIT", "US", "FORMAL", etc.
        var match = Regex.Match(text, @"([A-Z][A-Z\s]+)");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return text;
    }

    // Helper methods
    private static bool IsEntrySeparator(string line)
    {
        return line.StartsWith("——————————————") ||
               line.StartsWith("---------------") ||
               line.StartsWith("================");
    }

    private static bool IsHeadwordLine(string line)
    {
        // Headword lines contain the word without sense numbers
        return !IsSenseStart(line, out _, out _) &&
               !line.StartsWith("【") &&
               !line.StartsWith("...") &&
               !line.StartsWith("•") &&
               ContainsEnglishLetters(line) &&
               line.Length > 1 &&
               !line.Contains("→see:");
    }

    private static string ExtractHeadword(string line)
    {
        // Remove formatting symbols
        var cleaned = Regex.Replace(line, @"[★☆●○]+", " ").Trim();

        // Extract just the word part (before any IPA or other markers)
        var match = Regex.Match(cleaned, @"^([A-Za-z0-9\-']+)(?:\s+|$)");
        return match.Success ? match.Groups[1].Value.Trim() : cleaned;
    }

    public static bool IsSenseStart(string line, out int senseNumber, out string partOfSpeech)
    {
        senseNumber = 0;
        partOfSpeech = "unk";

        line = line.Trim();

        // Pattern: 1.PREFIX, 2.VERB, 3.DET, etc.
        var match = Regex.Match(line, @"^(?<num>\d+)\.(?<pos>[A-Z][A-Z\-]+)");
        if (match.Success)
        {
            if (int.TryParse(match.Groups["num"].Value, out int num))
                senseNumber = num;

            partOfSpeech = match.Groups["pos"].Value;
            return true;
        }

        return false;
    }

    private static bool ContainsEnglishLetters(string text)
    {
        return Regex.IsMatch(text, @"[A-Za-z]");
    }

    public static string RemoveChineseCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove ALL Chinese characters (Unicode range)
        var cleaned = Regex.Replace(text, @"[\u4E00-\u9FFF]", " ");

        // Remove Chinese punctuation and formatting marks
        cleaned = cleaned.Replace("。", ".")
            .Replace("，", ",")
            .Replace("；", ";")
            .Replace("：", ":")
            .Replace("！", "!")
            .Replace("？", "?")
            .Replace("（", "(")
            .Replace("）", ")")
            .Replace("【", "[")
            .Replace("】", "]")
            .Replace("、", ",")
            .Replace("…", "...")
            .Replace("——", "--")
            .Replace("～", "~")
            .Replace("「", "\"")
            .Replace("」", "\"")
            .Replace("『", "'")
            .Replace("』", "'")
            .Replace("〈", "<")
            .Replace("〉", ">")
            .Replace("《", "<")
            .Replace("》", ">");

        // Remove multiple spaces and trim
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    public static string NormalizePos(string pos)
    {
        if (string.IsNullOrWhiteSpace(pos))
            return "unk";

        var upperPos = pos.ToUpper();

        // Handle compound POS
        if (upperPos.Contains("-"))
        {
            var basePos = upperPos.Split('-')[0];
            return basePos switch
            {
                "N" or "N-COUNT" or "N-UNCOUNT" or "N-VAR" or "N-MASS" or "N-PLURAL" or "N-SING" => "noun",
                "V" or "VERB" or "V-ERG" or "V-PASSIVE" or "V-LINK" => "verb",
                "ADJ" or "ADJ-GRADED" or "ADJ-CLASSIF" => "adj",
                "ADV" or "ADV-GRADED" => "adv",
                "PREP" => "preposition",
                "CONJ" => "conjunction",
                "PRON" => "pronoun",
                "DET" => "determiner",
                "PHR" or "PHRASE" => "phrase",
                "ABBR" => "abbreviation",
                _ => basePos.ToLower()
            };
        }

        return upperPos switch
        {
            "N" or "NOUN" or "N-COUNT" or "N-UNCOUNT" or "N-VAR" => "noun",
            "V" or "VERB" => "verb",
            "ADJ" or "ADJECTIVE" or "ADJ-GRADED" => "adj",
            "ADV" or "ADVERB" => "adv",
            "PHRASE" or "PHR" => "phrase",
            "DETERMINER" or "DET" => "determiner",
            "PREPOSITION" or "PREP" => "preposition",
            "CONJUNCTION" or "CONJ" => "conjunction",
            "PRONOUN" or "PRON" => "pronoun",
            "INTERJECTION" or "INTERJ" => "interjection",
            "SUFFIX" => "suffix",
            "PREFIX" => "prefix",
            "ABBREVIATION" or "ABBR" => "abbreviation",
            "SYMBOL" => "symbol",
            "NUMERAL" or "NUM" => "numeral",
            "PHRASAL VERB" => "phrasal verb",
            "MODAL VERB" => "modal verb",
            "AUXILIARY VERB" => "auxiliary verb",
            _ => "unk"
        };
    }
}