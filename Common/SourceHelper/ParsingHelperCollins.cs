using System.Text.RegularExpressions;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Sources.Collins;

namespace DictionaryImporter.Common.SourceHelper;

internal static class ParsingHelperCollins
{
    public static ParsedDefinition BuildParsedDefinition(DictionaryEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return BuildFallbackParsedDefinition(entry);

        // Parse the Collins entry
        var parsed = ParseCollinsEntry(entry.RawFragmentLine);

        // Extract examples from multiple sources
        var examples = ExtractAllExamples(entry, parsed);

        // Extract POS - FIXED to get proper POS
        var partOfSpeech = ExtractPartOfSpeech(entry, parsed);

        // Extract sense number
        var senseNumber = ExtractSenseNumber(entry, parsed);

        // Extract domain
        var domain = ExtractDomain(entry, parsed);

        // Extract definition WITHOUT examples
        var cleanDefinition = ExtractCleanDefinition(entry, parsed, examples);

        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = cleanDefinition,
            RawFragment = entry.RawFragmentLine,
            SenseNumber = senseNumber,
            Domain = domain,
            UsageLabel = BuildUsageLabel(parsed),
            CrossReferences = parsed.CrossReferences?.ToList() ?? new List<CrossReference>(),
            Synonyms = parsed.Synonyms?.ToList() ?? new List<string>(),
            Alias = parsed.Alias,
            Examples = examples, // Store examples separately
            PartOfSpeech = partOfSpeech,
            IPA = parsed.IPA ?? ExtractIpaFromText(entry.RawFragmentLine ?? entry.Definition),
            GrammarInfo = parsed.GrammarInfo,
            UsageNote = parsed.UsageNote
        };
    }

    public static CollinsParsedData ParseCollinsEntry(string definition)
    {
        var data = new CollinsParsedData();

        if (string.IsNullOrWhiteSpace(definition))
            return data;

        // Parse sense number and POS - FIXED: Use improved parsing
        ParseSenseNumberAndPos(definition, data);

        // Extract ALL components
        data.DomainLabels = ExtractDomainLabels(definition)?.ToList() ?? new List<string>();
        data.UsagePatterns = ExtractUsagePatterns(definition)?.ToList() ?? new List<string>();
        data.Examples = ExtractCollinsExamples(definition)?.ToList() ?? new List<string>();
        data.CrossReferences = ExtractCrossReferences(definition)?.ToList() ?? new List<CrossReference>();
        data.Alias = ExtractAlias(definition);
        data.Synonyms = ExtractSynonyms(definition)?.ToList() ?? new List<string>();
        data.GrammarInfo = ExtractGrammarInfo(definition);
        data.UsageNote = ExtractUsageNote(definition);
        data.IPA = ExtractIpa(definition);

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

    private static void ParseSenseNumberAndPos(string text, CollinsParsedData data)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Look for pattern: 1.PREFIX, 1.VERB, etc.
        var match = Regex.Match(text, @"^(?<num>\d+)\.(?<pos>[A-Z][A-Z\-]+(?:\s+[A-Z\-]+)?)");
        if (match.Success)
        {
            if (int.TryParse(match.Groups["num"].Value, out int senseNum))
                data.SenseNumber = senseNum;

            var pos = match.Groups["pos"].Value.Trim();
            data.PartOfSpeech = CollinsExtractor.NormalizePos(pos);
        }
        else
        {
            data.SenseNumber = 1;
            data.PartOfSpeech = "unk";
        }
    }

    private static List<string> ExtractAllExamples(DictionaryEntry entry, CollinsParsedData parsed)
    {
        var examples = new List<string>();

        // 1. First check entry.Examples (already extracted by CollinsExtractor)
        if (entry.Examples?.Any() == true)
        {
            examples.AddRange(entry.Examples);
        }

        // 2. Then check parsed.Examples
        if (parsed.Examples?.Any() == true)
        {
            examples.AddRange(parsed.Examples);
        }

        // 3. Finally extract from raw fragment if still empty
        if (examples.Count == 0 && !string.IsNullOrWhiteSpace(entry.RawFragmentLine))
        {
            var extracted = ExtractCollinsExamples(entry.RawFragmentLine);
            examples.AddRange(extracted);
        }

        // Clean and validate examples
        return examples.Select(CleanExample)
                      .Where(e => !string.IsNullOrWhiteSpace(e))
                      .Where(e => e.Length > 10)
                      .Where(IsValidExample)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();
    }

    private static string ExtractPartOfSpeech(DictionaryEntry entry, CollinsParsedData parsed)
    {
        // Priority: parsed POS > entry POS
        if (!string.IsNullOrWhiteSpace(parsed.PartOfSpeech) && parsed.PartOfSpeech != "unk")
            return parsed.PartOfSpeech;

        if (!string.IsNullOrWhiteSpace(entry.PartOfSpeech))
            return CollinsExtractor.NormalizePos(entry.PartOfSpeech);

        return "unk";
    }

    private static int ExtractSenseNumber(DictionaryEntry entry, CollinsParsedData parsed)
    {
        if (parsed.SenseNumber > 0)
            return parsed.SenseNumber;

        return entry.SenseNumber > 0 ? entry.SenseNumber : 1;
    }

    private static string ExtractDomain(DictionaryEntry entry, CollinsParsedData parsed)
    {
        if (!string.IsNullOrWhiteSpace(parsed.PrimaryDomain))
            return parsed.PrimaryDomain;

        if (!string.IsNullOrWhiteSpace(entry.DomainLabel))
        {
            var domain = entry.DomainLabel;
            if (domain.Equals("BRIT", StringComparison.OrdinalIgnoreCase))
                return "UK";
            else if (domain.Equals("AM", StringComparison.OrdinalIgnoreCase))
                return "US";
            return domain;
        }

        return null;
    }

    private static string? ExtractCleanDefinition(DictionaryEntry entry, CollinsParsedData parsed, List<string> examples)
    {
        var definition = entry.Definition;

        if (string.IsNullOrWhiteSpace(definition))
            return definition;

        // Remove Chinese characters
        definition = CollinsExtractor.RemoveChineseCharacters(definition);

        // Remove example sentences from definition
        foreach (var example in examples)
        {
            var cleanExample = CleanExample(example);
            if (!string.IsNullOrWhiteSpace(cleanExample) && definition.Contains(cleanExample))
            {
                definition = definition.Replace(cleanExample, "").Trim();
            }
        }

        // Remove any remaining example markers
        definition = definition.Replace("...", "").Replace("•", "").Trim();

        // Remove bracket content
        definition = Regex.Replace(definition, @"【[^】]*】", " ");

        // Clean up
        definition = Regex.Replace(definition, @"\s+", " ").Trim();

        // Ensure proper ending
        if (!string.IsNullOrEmpty(definition) &&
            !definition.EndsWith(".") &&
            !definition.EndsWith("!") &&
            !definition.EndsWith("?"))
        {
            definition += ".";
        }

        return definition;
    }

    public static IReadOnlyList<string> ExtractCollinsExamples(string text)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return examples;

        // Split by lines
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        foreach (var line in lines)
        {
            // Extract examples from various patterns
            ExtractExamplesFromLine(line, examples);
        }

        return examples.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ExtractExamplesFromLine(string line, List<string> examples)
    {
        // Pattern 1: Lines starting with ...
        if (line.StartsWith("..."))
        {
            var example = ExtractEnglishPart(line.Substring(3));
            if (!string.IsNullOrWhiteSpace(example) && IsValidExample(example))
            {
                examples.Add(CleanExample(example));
            }
            return;
        }

        // Pattern 2: Lines starting with •
        if (line.StartsWith("•"))
        {
            var example = ExtractEnglishPart(line.Substring(1));
            if (!string.IsNullOrWhiteSpace(example) && IsValidExample(example))
            {
                examples.Add(CleanExample(example));
            }
            return;
        }

        // Pattern 3: English sentences in the line
        var englishParts = ExtractEnglishSentences(line);
        foreach (var part in englishParts)
        {
            if (!string.IsNullOrWhiteSpace(part) && IsValidExample(part))
            {
                examples.Add(CleanExample(part));
            }
        }
    }

    private static string ExtractEnglishPart(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Extract text before Chinese characters
        var result = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            if (c >= '\u4E00' && c <= '\u9FFF')
                break;
            result.Append(c);
        }

        return result.ToString().Trim();
    }

    private static List<string> ExtractEnglishSentences(string text)
    {
        var sentences = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return sentences;

        // Extract English sentences (capital letter, ends with punctuation)
        var pattern = @"[A-Z][^.!?]*[.!?]";
        var matches = Regex.Matches(text, pattern);

        foreach (Match match in matches)
        {
            var sentence = match.Value.Trim();
            // Check if it's an English sentence (not Chinese, not too short)
            if (sentence.Length > 10 && Regex.IsMatch(sentence, @"[A-Za-z]"))
            {
                // Remove any Chinese characters
                sentence = CollinsExtractor.RemoveChineseCharacters(sentence);
                sentences.Add(sentence);
            }
        }

        return sentences;
    }

    private static bool IsValidExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example) || example.Length < 10)
            return false;

        // Must contain English letters
        if (!Regex.IsMatch(example, @"[A-Za-z]"))
            return false;

        // Must look like a sentence
        if (!char.IsUpper(example[0]))
            return false;

        // Must end with proper punctuation
        if (!example.EndsWith(".") && !example.EndsWith("!") && !example.EndsWith("?"))
            return false;

        // Not a definition
        if (example.StartsWith("If ", StringComparison.OrdinalIgnoreCase) ||
            example.StartsWith("To ", StringComparison.OrdinalIgnoreCase) ||
            example.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
            example.StartsWith("A ", StringComparison.OrdinalIgnoreCase) ||
            example.StartsWith("An ", StringComparison.OrdinalIgnoreCase) ||
            example.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ||
            example.StartsWith("You ", StringComparison.OrdinalIgnoreCase) ||
            example.Contains(" means ") ||
            example.Contains(" is ") ||
            example.Contains(" are ") ||
            example.Contains(" refers to ") ||
            example.Contains(" describes "))
        {
            return false;
        }

        return true;
    }

    private static string CleanExample(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters
        var cleaned = CollinsExtractor.RemoveChineseCharacters(text);

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

    private static IReadOnlyList<string> ExtractDomainLabels(string text)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 【语域标签】
        foreach (Match m in Regex.Matches(text, @"【语域标签】：\s*(?<label>[^】]+)"))
        {
            var labelText = m.Groups["label"].Value;
            // Extract English labels
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

    private static IReadOnlyList<string> ExtractUsagePatterns(string text)
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

    public static string? ExtractAlias(string text)
    {
        var match = Regex.Match(text, @"also\s+(?:called|known as|spelled)\s+['""]?(?<alias>[A-Za-z0-9\-']+)['""]?",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["alias"].Value : null;
    }

    private static string? ExtractGrammarInfo(string text)
    {
        var patterns = new List<string>();

        foreach (Match m in Regex.Matches(text, @"【搭配模式】：\s*(.+?)(?:\s|$|】)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        foreach (Match m in Regex.Matches(text, @"【语法信息】：\s*(.+?)(?:\s|$|】)"))
            patterns.Add(CleanPattern(m.Groups[1].Value));

        return patterns.Any() ? string.Join("; ", patterns) : null;
    }

    private static string? ExtractUsageNote(string text)
    {
        var match = Regex.Match(text, @"【注意】：\s*(.+)");
        return match.Success ? CleanPattern(match.Groups[1].Value) : null;
    }

    private static string? ExtractIpa(string text)
    {
        // Look for IPA in slashes
        var match = Regex.Match(text, @"/([^/]+)/");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractIpaFromText(string text)
    {
        // Look for IPA in slashes
        var match = Regex.Match(text, @"/([^/]+)/");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static IReadOnlyList<CrossReference> ExtractCrossReferences(string text)
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

    private static string? BuildUsageLabel(CollinsParsedData data)
    {
        return data.UsagePatterns?.Any() == true
            ? string.Join(", ", data.UsagePatterns.Distinct())
            : null;
    }

    private static ParsedDefinition BuildFallbackParsedDefinition(DictionaryEntry entry)
    {
        // Try to extract examples even in fallback
        var examples = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Definition))
        {
            examples = ExtractCollinsExamples(entry.Definition)?.ToList() ?? new List<string>();
        }

        // Clean examples
        examples = examples.Select(CleanExample)
                          .Where(e => !string.IsNullOrWhiteSpace(e))
                          .ToList();

        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = CleanDefinitionText(entry.Definition ?? string.Empty),
            RawFragment = entry.RawFragmentLine ?? string.Empty,
            SenseNumber = entry.SenseNumber,
            Domain = null,
            UsageLabel = null,
            CrossReferences = new List<CrossReference>(),
            Synonyms = new List<string>(),
            Alias = null,
            Examples = examples,
            PartOfSpeech = !string.IsNullOrEmpty(entry.PartOfSpeech)
                ? CollinsExtractor.NormalizePos(entry.PartOfSpeech)
                : "unk",
            IPA = null,
            GrammarInfo = null,
            UsageNote = null
        };
    }

    private static string CleanDefinitionText(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return definition;

        // Remove Chinese characters
        var cleaned = CollinsExtractor.RemoveChineseCharacters(definition);

        // Remove any markers and clean up
        cleaned = Regex.Replace(cleaned, @"【[^】]*】", " ")
                      .Replace("  ", " ")
                      .Replace(" ; ; ", " ")
                      .Replace(" ; ", " ")
                      .Trim();

        // Remove any remaining example markers
        cleaned = cleaned.Replace("...", "").Replace("•", "");

        return cleaned;
    }
}