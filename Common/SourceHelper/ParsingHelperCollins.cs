using DictionaryImporter.Domain.Models;
using DictionaryImporter.Sources.Collins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Common.SourceHelper;

internal static class ParsingHelperCollins
{
    #region Core Entry Parsing

    public static ParsedDefinition BuildParsedDefinition(DictionaryEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return BuildFallbackParsedDefinition(entry);

        var parsed = ParseCollinsEntry(entry.Definition);

        var fullDefinition = BuildFullDefinition(parsed);

        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = fullDefinition,
            RawFragment = entry.Definition,
            SenseNumber = parsed.SenseNumber,
            Domain = parsed.PrimaryDomain,
            UsageLabel = BuildUsageLabel(parsed),
            CrossReferences = parsed.CrossReferences?.ToList() ?? new List<CrossReference>(),
            Synonyms = null,
            Alias = parsed.Alias,
            Examples = parsed.Examples?.ToList() ?? new List<string>(),
            PartOfSpeech = parsed.PartOfSpeech
        };
    }

    public static CollinsParsedData ParseCollinsEntry(string definition)
    {
        var data = new CollinsParsedData();

        if (string.IsNullOrWhiteSpace(definition))
            return data;

        // Clean the definition first
        var cleanedDefinition = CollinsExtractor.RemoveChineseCharacters(definition);

        // Parse sense number and POS
        ParseSenseNumberAndPOS(cleanedDefinition, data);

        // Extract components
        data.MainDefinition = ExtractMainDefinition(cleanedDefinition);
        data.DomainLabels = ExtractDomainLabels(definition)?.ToList() ?? new List<string>();
        data.UsagePatterns = ExtractUsagePatterns(definition)?.ToList() ?? new List<string>();
        data.Examples = ExtractExamples(cleanedDefinition)?.ToList() ?? new List<string>();
        data.CrossReferences = ExtractCrossReferences(cleanedDefinition)?.ToList() ?? new List<CrossReference>();
        data.Alias = ExtractAlias(cleanedDefinition);

        data.CleanDefinition = CleanCollinsDefinition(cleanedDefinition);

        // Set primary domain from labels
        if (data.DomainLabels.Any())
        {
            data.PrimaryDomain = data.DomainLabels.First();
        }

        return data;
    }

    private static void ParseSenseNumberAndPOS(string definition, CollinsParsedData data)
    {
        var lines = definition.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return;

        var firstLine = lines[0];

        // Look for pattern like "1.N-VAR" or "1.VERB"
        var match = Regex.Match(firstLine, @"^(?<num>\d+)\.(?<pos>[A-Z][A-Z\-]+)");
        if (match.Success)
        {
            data.SenseNumber = int.Parse(match.Groups["num"].Value);
            data.PartOfSpeech = CollinsExtractor.NormalizePos(match.Groups["pos"].Value);
            return;
        }

        // Look for pattern like "1.ADV" (without hyphen)
        match = Regex.Match(firstLine, @"^(?<num>\d+)\.([A-Z]+)");
        if (match.Success)
        {
            data.SenseNumber = int.Parse(match.Groups["num"].Value);
            data.PartOfSpeech = CollinsExtractor.NormalizePos(match.Groups[2].Value);
            return;
        }

        // Default values
        data.SenseNumber = 1;
        data.PartOfSpeech = "unk";
    }

    private static string BuildFullDefinition(CollinsParsedData parsed)
    {
        var parts = new List<string>();

        // Add sense header if available
        if (parsed.SenseNumber > 0 && !string.IsNullOrEmpty(parsed.PartOfSpeech))
        {
            parts.Add($"{parsed.SenseNumber}.{parsed.PartOfSpeech.ToUpper()}");
        }

        // Add main definition
        if (!string.IsNullOrEmpty(parsed.MainDefinition))
        {
            parts.Add(parsed.MainDefinition);
        }

        // Add usage patterns
        if (parsed.UsagePatterns?.Any() == true)
        {
            parts.Add($"【Patterns】{string.Join("; ", parsed.UsagePatterns)}");
        }

        // Add examples
        if (parsed.Examples?.Any() == true)
        {
            parts.Add("【Examples】");
            parts.AddRange(parsed.Examples.Select(e => $"• {e}"));
        }

        // Add cross references
        if (parsed.CrossReferences?.Any() == true)
        {
            parts.Add("【See Also】" + string.Join(", ",
                parsed.CrossReferences.Select(r => r.TargetWord)));
        }

        return string.Join("\n", parts);
    }

    #endregion Core Entry Parsing

    #region Definitions

    public static string ExtractMainDefinition(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        foreach (var line in lines)
        {
            // Skip lines that are clearly not the main definition
            if (line.StartsWith("【") ||
                line.StartsWith("...") ||
                line.StartsWith("•") ||
                Regex.IsMatch(line, @"^\d+\.[A-Z]") ||
                line.Contains("→see:"))
                continue;

            // Look for English definition sentences
            if (line.StartsWith("If ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("You ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("To ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("A ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("An ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ||
                (char.IsUpper(line.FirstOrDefault()) && line.Length > 10))
            {
                // Clean up the line
                var cleaned = line.Replace(" ; ; ", " ")
                                 .Replace(" ; ", " ")
                                 .Replace("  ", " ")
                                 .Trim();

                // Ensure it ends with punctuation
                if (!string.IsNullOrEmpty(cleaned) &&
                    !cleaned.EndsWith(".") &&
                    !cleaned.EndsWith("!") &&
                    !cleaned.EndsWith("?"))
                {
                    cleaned += ".";
                }

                return cleaned;
            }
        }

        // If no main definition found, return first non-header line
        foreach (var line in lines)
        {
            if (!Regex.IsMatch(line, @"^\d+\.[A-Z]") &&
                !line.StartsWith("【") &&
                !line.StartsWith("...") &&
                !line.StartsWith("•"))
            {
                return line;
            }
        }

        return string.Empty;
    }

    #endregion Definitions

    #region Examples

    public static IReadOnlyList<string> ExtractExamples(string text)
    {
        var examples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));

        foreach (var line in lines)
        {
            // ...example pattern
            if (line.StartsWith("..."))
            {
                var example = CleanExample(line[3..]);
                if (!string.IsNullOrWhiteSpace(example))
                    examples.Add(example);
                continue;
            }

            // Bullet point examples
            if (line.StartsWith("•"))
            {
                var example = CleanExample(line[1..]);
                if (!string.IsNullOrWhiteSpace(example))
                    examples.Add(example);
                continue;
            }

            // Full English sentence pattern
            if (Regex.IsMatch(line, @"^[A-Z][^.!?]*[.!?]$") &&
                line.Length > 10 &&
                !line.StartsWith("【"))
            {
                var example = CleanExample(line);
                if (!string.IsNullOrWhiteSpace(example))
                    examples.Add(example);
            }
        }

        return examples.ToList();
    }

    private static string CleanExample(string text)
    {
        var cleaned = text.Replace("  ", " ")
                         .Trim();

        // Ensure proper punctuation
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

    #endregion Examples

    #region Labels & Usage

    public static IReadOnlyList<string> ExtractDomainLabels(string text)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract from 【语域标签】patterns
        foreach (Match m in Regex.Matches(text, @"【语域标签】：\s*(?<label>.+?)(?:\s+[^\s】]+)?】"))
        {
            var labelText = m.Groups["label"].Value;
            // Extract English part
            var engMatch = Regex.Match(labelText, @"([A-Z]+)");
            if (engMatch.Success)
                labels.Add(engMatch.Groups[1].Value);
        }

        // Extract from 【FIELD标签】patterns
        foreach (Match m in Regex.Matches(text, @"【FIELD标签】：\s*(?<label>.+?)(?:\s+[^\s】]+)?】"))
        {
            var labelText = m.Groups["label"].Value;
            var engMatch = Regex.Match(labelText, @"([A-Z]+)");
            if (engMatch.Success)
                labels.Add(engMatch.Groups[1].Value);
        }

        // Extract from inline markers
        if (text.Contains("主美") || text.Contains("AM") || text.Contains("美国英语"))
            labels.Add("US");
        if (text.Contains("主英") || text.Contains("BRIT") || text.Contains("英国英语"))
            labels.Add("UK");
        if (text.Contains("FORMAL") || text.Contains("正式"))
            labels.Add("FORMAL");
        if (text.Contains("INFORMAL") || text.Contains("非正式"))
            labels.Add("INFORMAL");
        if (text.Contains("VERY RUDE") || text.Contains("OFFENSIVE") || text.Contains("冒犯"))
            labels.Add("OFFENSIVE");
        if (text.Contains("JOURNALISM") || text.Contains("新闻"))
            labels.Add("JOURNALISM");
        if (text.Contains("BUSINESS") || text.Contains("商"))
            labels.Add("BUSINESS");

        return labels.ToList();
    }

    public static IReadOnlyList<string> ExtractUsagePatterns(string text)
    {
        var patterns = new List<string>();

        // 【搭配模式】：usu N for n
        foreach (Match m in Regex.Matches(text, @"【搭配模式】：\s*(.+?)(?:\s|$)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        // 【语法信息】：V n
        foreach (Match m in Regex.Matches(text, @"【语法信息】：\s*(.+?)(?:\s|$)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        return patterns;
    }

    private static string CleanPattern(string pattern)
    {
        return CollinsExtractor.RemoveChineseCharacters(pattern)
            .Replace("  ", " ")
            .Trim();
    }

    private static string ExtractAlias(string text)
    {
        // Look for "also called" or "also known as"
        var match = Regex.Match(text, @"also\s+(?:called|known as)\s+(?<alias>\w+)",
            RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups["alias"].Value;

        return null;
    }

    #endregion Labels & Usage

    #region Cross References

    public static IReadOnlyList<CrossReference> ExtractCrossReferences(string text)
    {
        var refs = new List<CrossReference>();

        // →see: xxx
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

    #endregion Cross References

    #region Cleaning

    public static string CleanCollinsDefinition(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !l.StartsWith("【"))
            .Where(l => !l.Contains("→see:"))
            .Where(l => !l.StartsWith("..."))
            .Where(l => !l.StartsWith("•"))
            .ToList();

        var joined = string.Join(" ", lines);

        // Ensure proper punctuation
        if (!string.IsNullOrEmpty(joined) &&
            !joined.EndsWith(".") &&
            !joined.EndsWith("!") &&
            !joined.EndsWith("?"))
        {
            joined += ".";
        }

        return joined;
    }

    #endregion Cleaning

    #region Fallback

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
            Synonyms = null,
            Alias = null,
            PartOfSpeech = entry.PartOfSpeech
        };
    }

    private static string BuildUsageLabel(CollinsParsedData data)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(data.PartOfSpeech) && data.PartOfSpeech != "unk")
            parts.Add(data.PartOfSpeech.ToUpper());

        parts.AddRange(data.DomainLabels ?? new List<string>());

        if (data.UsagePatterns?.Any() == true)
            parts.AddRange(data.UsagePatterns);

        return parts.Count > 0 ? string.Join(", ", parts.Distinct()) : null;
    }

    #endregion Fallback
}

public class CollinsParsedData
{
    public int SenseNumber { get; set; } = 1;
    public string PartOfSpeech { get; set; } = "unk";
    public string MainDefinition { get; set; } = string.Empty;
    public string CleanDefinition { get; set; } = string.Empty;
    public List<string> DomainLabels { get; set; } = new();
    public string PrimaryDomain { get; set; }
    public List<string> UsagePatterns { get; set; } = new();
    public List<string> Examples { get; set; } = new();
    public List<CrossReference> CrossReferences { get; set; } = new();
    public string Alias { get; set; }
}