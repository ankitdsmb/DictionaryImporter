using DictionaryImporter.Domain.Models;
using DictionaryImporter.Sources.Collins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Common.SourceHelper;

internal static class ParsingHelperCollins
{
    public static ParsedDefinition BuildParsedDefinition(DictionaryEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return BuildFallbackParsedDefinition(entry);

        var parsed = ParseCollinsEntry(entry.RawFragment ?? entry.Definition);

        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = entry.Definition,
            RawFragment = entry.RawFragment,
            SenseNumber = parsed.SenseNumber,
            Domain = parsed.PrimaryDomain,
            UsageLabel = BuildUsageLabel(parsed),
            CrossReferences = parsed.CrossReferences?.ToList() ?? new List<CrossReference>(),
            Synonyms = parsed.Synonyms?.ToList() ?? new List<string>(),
            Alias = parsed.Alias,
            Examples = parsed.Examples?.ToList() ?? new List<string>(), // This should populate the Examples
            PartOfSpeech = parsed.PartOfSpeech,
            IPA = parsed.IPA,
            GrammarInfo = parsed.GrammarInfo,
            UsageNote = parsed.UsageNote
        };
    }

    public static CollinsParsedData ParseCollinsEntry(string definition)
    {
        var data = new CollinsParsedData();

        if (string.IsNullOrWhiteSpace(definition))
            return data;

        // Parse sense number and POS
        ParseSenseNumberAndPOS(definition, data);

        // Extract ALL components
        data.DomainLabels = ExtractDomainLabels(definition)?.ToList() ?? new List<string>();
        data.UsagePatterns = ExtractUsagePatterns(definition)?.ToList() ?? new List<string>();
        data.Examples = ExtractCollinsExamples(definition)?.ToList() ?? new List<string>(); // Use improved extraction
        data.CrossReferences = ExtractCrossReferences(definition)?.ToList() ?? new List<CrossReference>();
        data.Alias = ExtractAlias(definition);
        data.Synonyms = ExtractSynonyms(definition)?.ToList() ?? new List<string>();
        data.GrammarInfo = ExtractGrammarInfo(definition);
        data.UsageNote = ExtractUsageNote(definition);
        data.IPA = ExtractIPA(definition);

        // Set primary domain
        if (data.DomainLabels.Any())
        {
            data.PrimaryDomain = data.DomainLabels.First();
            if (data.PrimaryDomain.Equals("BRIT", StringComparison.OrdinalIgnoreCase))
                data.PrimaryDomain = "UK";
            else if (data.PrimaryDomain.Equals("AM", StringComparison.OrdinalIgnoreCase))
                data.PrimaryDomain = "US";
        }

        return data;
    }

    private static void ParseSenseNumberAndPOS(string text, CollinsParsedData data)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Look for pattern at beginning
        var match = Regex.Match(text, @"^(?<num>\d+)\.(?<pos>[A-Z][A-Z\-]+)");
        if (match.Success)
        {
            data.SenseNumber = int.Parse(match.Groups["num"].Value);
            data.PartOfSpeech = CollinsExtractor.NormalizePos(match.Groups["pos"].Value);
        }
        else
        {
            data.SenseNumber = 1;
            data.PartOfSpeech = "unk";
        }
    }

    public static IReadOnlyList<string> ExtractCollinsExamples(string text)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return examples;

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        bool inExampleSection = false;

        foreach (var line in lines)
        {
            // Skip definition lines
            if (IsDefinitionLine(line))
                continue;

            // Check for example markers
            if (line.StartsWith("...") || line.StartsWith("•"))
            {
                inExampleSection = true;
                var example = ExtractExampleFromLine(line);
                if (!string.IsNullOrWhiteSpace(example) && IsValidExample(example))
                {
                    examples.Add(example);
                }
            }
            // If we're in example section and this is a continuation line
            else if (inExampleSection && IsExampleContinuation(line))
            {
                var example = CleanExample(line);
                if (!string.IsNullOrWhiteSpace(example) && IsValidExample(example))
                {
                    examples.Add(example);
                }
            }
            else
            {
                inExampleSection = false;
            }
        }

        return examples.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsDefinitionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var cleanLine = CollinsExtractor.RemoveChineseCharacters(line);

        // Check if it looks like a definition
        return cleanLine.StartsWith("If ", StringComparison.OrdinalIgnoreCase) ||
               cleanLine.StartsWith("To ", StringComparison.OrdinalIgnoreCase) ||
               cleanLine.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
               cleanLine.StartsWith("A ", StringComparison.OrdinalIgnoreCase) ||
               cleanLine.StartsWith("An ", StringComparison.OrdinalIgnoreCase) ||
               cleanLine.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ||
               cleanLine.StartsWith("You ", StringComparison.OrdinalIgnoreCase) ||
               cleanLine.Contains(" means ") ||
               cleanLine.Contains(" is ") ||
               cleanLine.Contains(" are ") ||
               cleanLine.Contains(" refers to ") ||
               cleanLine.Contains(" describes ");
    }

    private static string ExtractExampleFromLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Remove example markers
        var example = line.StartsWith("...") ? line.Substring(3) :
                     line.StartsWith("•") ? line.Substring(1) : line;

        return CleanExample(example);
    }

    private static bool IsExampleContinuation(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length < 10)
            return false;

        // Check if it looks like an English sentence
        var cleanLine = CollinsExtractor.RemoveChineseCharacters(line);

        return char.IsUpper(cleanLine[0]) &&
               (cleanLine.EndsWith(".") || cleanLine.EndsWith("!") || cleanLine.EndsWith("?")) &&
               cleanLine.Length > 20 &&
               !IsDefinitionLine(cleanLine);
    }

    private static bool IsValidExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example) || example.Length < 15)
            return false;

        // Check it has English letters
        if (!Regex.IsMatch(example, @"[A-Za-z]"))
            return false;

        // Check it's not a definition
        if (IsDefinitionLine(example))
            return false;

        return true;
    }

    private static string CleanExample(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters
        var cleaned = CollinsExtractor.RemoveChineseCharacters(text)
            .Replace("  ", " ")
            .Trim();

        // Remove any leftover punctuation at edges
        cleaned = Regex.Replace(cleaned, @"^[,\s()""]+|[,\s()""]+$", "");

        // Replace multiple periods with single period
        cleaned = Regex.Replace(cleaned, @"\.{2,}", ".");

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

    public static IReadOnlyList<string> ExtractDomainLabels(string text)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 【语域标签】
        foreach (Match m in Regex.Matches(text, @"【语域标签】：\s*(?<label>[^】]+)"))
        {
            var labelText = m.Groups["label"].Value;
            var engMatch = Regex.Match(labelText, @"([A-Z][A-Z\s]+)");
            if (engMatch.Success)
                labels.Add(engMatch.Groups[1].Value.Trim());
        }

        // 【FIELD标签】
        foreach (Match m in Regex.Matches(text, @"【FIELD标签】：\s*(?<label>[^】]+)"))
        {
            var labelText = m.Groups["label"].Value;
            var engMatch = Regex.Match(labelText, @"([A-Z][A-Z\s]+)");
            if (engMatch.Success)
                labels.Add(engMatch.Groups[1].Value.Trim());
        }

        return labels.ToList();
    }

    public static IReadOnlyList<string> ExtractUsagePatterns(string text)
    {
        var patterns = new List<string>();

        foreach (Match m in Regex.Matches(text, @"【搭配模式】：\s*(.+?)(?:\s|$|】)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        foreach (Match m in Regex.Matches(text, @"【语法信息】：\s*(.+?)(?:\s|$|】)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        return patterns;
    }

    private static string CleanPattern(string pattern)
    {
        return CollinsExtractor.RemoveChineseCharacters(pattern)
            .Replace("  ", " ")
            .Trim();
    }

    public static IReadOnlyList<string> ExtractSynonyms(string text)
    {
        var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Look for "synonym" patterns
        var matches = Regex.Matches(text, @"synonym(?:s|ous)?\s+(?:of|for)?\s+['""]?([A-Za-z0-9\-']+)['""]?",
            RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (match.Groups[1].Success)
                synonyms.Add(match.Groups[1].Value.Trim());
        }

        return synonyms.ToList();
    }

    public static string ExtractAlias(string text)
    {
        var match = Regex.Match(text, @"also\s+(?:called|known as|spelled)\s+['""]?(?<alias>[A-Za-z0-9\-']+)['""]?",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["alias"].Value : null;
    }

    public static string ExtractGrammarInfo(string text)
    {
        var patterns = new List<string>();

        foreach (Match m in Regex.Matches(text, @"【搭配模式】：\s*(.+?)(?:\s|$|】)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        foreach (Match m in Regex.Matches(text, @"【语法信息】：\s*(.+?)(?:\s|$|】)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        return patterns.Any() ? string.Join("; ", patterns) : null;
    }

    public static string ExtractUsageNote(string text)
    {
        var match = Regex.Match(text, @"【注意】：\s*(.+)");
        return match.Success ? CleanPattern(match.Groups[1].Value) : null;
    }

    public static string ExtractIPA(string text)
    {
        // Look for IPA in slashes
        var match = Regex.Match(text, @"/([^/]+)/");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public static IReadOnlyList<CrossReference> ExtractCrossReferences(string text)
    {
        var refs = new List<CrossReference>();

        foreach (Match m in Regex.Matches(text, @"→see:\s*(?<w>[^;\n]+)", RegexOptions.IgnoreCase))
        {
            var words = m.Groups["w"].Value.Split(',')
                .Select(w => w.Trim())
                .Where(w => !string.IsNullOrWhiteSpace(w));

            foreach (var word in words)
            {
                refs.Add(new CrossReference
                {
                    TargetWord = word,
                    ReferenceType = "See"
                });
            }
        }

        return refs;
    }

    private static string BuildUsageLabel(CollinsParsedData data)
    {
        return data.UsagePatterns?.Any() == true
            ? string.Join(", ", data.UsagePatterns.Distinct())
            : null;
    }

    public static ParsedDefinition BuildFallbackParsedDefinition(DictionaryEntry entry)
    {
        return new ParsedDefinition
        {
            MeaningTitle = entry.Word,
            Definition = entry.Definition ?? string.Empty,
            RawFragment = entry.Definition ?? string.Empty,
            SenseNumber = entry.SenseNumber,
            Domain = null,
            UsageLabel = null,
            CrossReferences = new List<CrossReference>(),
            Synonyms = new List<string>(),
            Alias = null,
            Examples = new List<string>(),
            PartOfSpeech = entry.PartOfSpeech,
            IPA = null,
            GrammarInfo = null,
            UsageNote = null
        };
    }
}