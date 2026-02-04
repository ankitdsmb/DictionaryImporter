using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Sources.Oxford.Parsing;

public sealed class OxfordDefinitionParser(ILogger<OxfordDefinitionParser> logger)
    : ISourceDictionaryDefinitionParser
{
    private readonly ILogger<OxfordDefinitionParser> _logger = logger;

    public string SourceCode => "ENG_OXFORD";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        if (entry == null)
        {
            return new[] { CreateFallbackParsedDefinition(entry) };
        }

        try
        {
            // Use Definition as primary source, RawFragment as fallback
            var sourceText = !string.IsNullOrWhiteSpace(entry.Definition)
                ? entry.Definition
                : entry.RawFragmentLine ?? string.Empty;

            var parsedData = ParseOxfordDefinition(sourceText, entry);
            return new[] { parsedData };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Oxford definition for {Word}", entry.Word);
            return new[] { CreateFallbackParsedDefinition(entry) };
        }
    }

    private ParsedDefinition ParseOxfordDefinition(string definition, DictionaryEntry entry)
    {
        var (cleanDefinition, domain, usage, examples, crossRefs, synonyms) =
            ExtractAllComponents(definition, entry.Word);

        // Ensure we have a definition - use RawFragment as last resort
        if (string.IsNullOrWhiteSpace(cleanDefinition) && !string.IsNullOrWhiteSpace(entry.RawFragmentLine))
        {
            cleanDefinition = ExtractDefinitionFromRawFragment(entry.RawFragmentLine);
        }

        // Ensure sense number is at least 1
        var senseNumber = entry.SenseNumber > 0 ? entry.SenseNumber : 1;

        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = cleanDefinition ?? string.Empty,
            RawFragment = entry.RawFragmentLine,
            SenseNumber = senseNumber,
            Domain = domain,
            UsageLabel = usage ?? entry.PartOfSpeech,
            CrossReferences = crossRefs,
            Synonyms = synonyms,
            Alias = null,
            SourceCode = entry.SourceCode
        };
    }

    private static (string definition, string? domain, string? usage,
        IReadOnlyList<string> examples, IReadOnlyList<CrossReference> crossRefs,
        IReadOnlyList<string>? synonyms) ExtractAllComponents(string definition, string headword)
    {
        var lines = definition.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string? domain = null;
        string? usage = null;
        var definitionLines = new List<string>();
        var examples = new List<string>();
        var inExamplesSection = false;
        var inEtymologySection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("【Label】"))
            {
                var labelContent = trimmed["【Label】".Length..].Trim();
                (domain, usage) = ParseLabelContent(labelContent);
                continue;
            }

            if (trimmed.StartsWith("【Examples】"))
            {
                inExamplesSection = true;
                inEtymologySection = false;
                continue;
            }

            if (trimmed.StartsWith("【Etymology】"))
            {
                inEtymologySection = true;
                inExamplesSection = false;
                continue;
            }

            if (trimmed.StartsWith("【"))
            {
                // Any other section ends current sections
                inExamplesSection = false;
                inEtymologySection = false;
                continue;
            }

            if (inExamplesSection && trimmed.StartsWith("»"))
            {
                var example = CleanExampleText(trimmed[1..].Trim());
                if (!string.IsNullOrWhiteSpace(example))
                    examples.Add(example);
                continue;
            }

            if (inEtymologySection || inExamplesSection)
                continue;

            // This is definition text
            if (!string.IsNullOrWhiteSpace(trimmed) &&
                !trimmed.StartsWith("»") &&
                !trimmed.StartsWith("--›"))
            {
                definitionLines.Add(trimmed);
            }
        }

        var cleanDefinition = CleanDefinitionText(string.Join(" ", definitionLines));
        var crossRefs = ExtractCrossReferences(cleanDefinition);
        var synonyms = ExtractSynonyms(cleanDefinition, examples, headword);

        return (cleanDefinition, domain, usage, examples, crossRefs, synonyms);
    }

    private static (string? domain, string? usage) ParseLabelContent(string labelContent)
    {
        var domain = new List<string>();
        var usage = new List<string>();

        var parts = labelContent.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var cleanPart = part.Trim().Trim('[', ']');
            if (string.IsNullOrWhiteSpace(cleanPart))
                continue;

            var lowerPart = cleanPart.ToLowerInvariant();

            // Usage labels
            if (lowerPart == "informal" || lowerPart == "formal" ||
                lowerPart == "dated" || lowerPart == "archaic" ||
                lowerPart == "slang" || lowerPart == "humorous" ||
                lowerPart == "literary" || lowerPart == "technical")
            {
                usage.Add(cleanPart);
            }
            else
            {
                // Assume it's a domain
                domain.Add(cleanPart);
            }
        }

        return (
            domain.Count > 0 ? string.Join("; ", domain) : null,
            usage.Count > 0 ? string.Join("; ", usage) : null
        );
    }

    private static string CleanDefinitionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var clean = text;

        // Remove any remaining section markers
        clean = Regex.Replace(clean, @"【[^】]*】", "");

        // Remove example markers
        clean = Regex.Replace(clean, @"^»\s*", "", RegexOptions.Multiline);

        // Remove cross-reference markers
        clean = Regex.Replace(clean, @"--›.*", "");

        // Remove formatting artifacts
        clean = Regex.Replace(clean, @"[•▶»◘›♦\-]", " ");

        // Clean whitespace
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        // Ensure proper sentence ending
        if (!string.IsNullOrWhiteSpace(clean) &&
            !clean.EndsWith(".") && !clean.EndsWith("!") && !clean.EndsWith("?") &&
            clean.Length > 10)
        {
            clean += ".";
        }

        return clean;
    }

    private static string CleanExampleText(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        var clean = example;

        // Remove Chinese characters
        clean = Regex.Replace(clean, @"[\u4e00-\u9fff]", "");

        // Remove brackets and their content
        clean = Regex.Replace(clean, @"\[[^\]]*\]", "");

        // Remove formatting
        clean = Regex.Replace(clean, @"[•◘♦]", " ");

        // Clean up
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        // Capitalize first letter if needed
        if (clean.Length > 0 && char.IsLower(clean[0]))
        {
            clean = char.ToUpper(clean[0]) + clean[1..];
        }

        return clean;
    }

    private static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
    {
        var crossRefs = new List<CrossReference>();

        if (string.IsNullOrWhiteSpace(definition))
            return crossRefs;

        // Pattern 1: --› see word
        var seeMatches = Regex.Matches(definition, @"--›\s*(?:see|cf\.?|compare)\s+([A-Za-z\-']+)");
        foreach (Match match in seeMatches)
        {
            if (match.Groups[1].Success)
            {
                crossRefs.Add(new CrossReference
                {
                    TargetWord = match.Groups[1].Value.Trim(),
                    ReferenceType = "SeeAlso"
                });
            }
        }

        // Pattern 2: variant of word
        var variantMatches = Regex.Matches(definition,
            @"(?:variant of|another term for|also called)\s+([A-Za-z\-']+)");
        foreach (Match match in variantMatches)
        {
            if (match.Groups[1].Success)
            {
                crossRefs.Add(new CrossReference
                {
                    TargetWord = match.Groups[1].Value.Trim(),
                    ReferenceType = "Variant"
                });
            }
        }

        return crossRefs;
    }

    private static IReadOnlyList<string>? ExtractSynonyms(string definition,
        IReadOnlyList<string> examples, string headword)
    {
        var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check definition for synonym patterns
        var synonymPatterns = new[]
        {
            @"\b(?:also called|also known as|synonymous with)\s+([A-Za-z\-']+)",
            @"\b([A-Za-z\-']+)\s+(?:or|and)\s+([A-Za-z\-']+)\b"
        };

        foreach (var pattern in synonymPatterns)
        {
            foreach (Match match in Regex.Matches(definition, pattern, RegexOptions.IgnoreCase))
            {
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success)
                    {
                        var word = match.Groups[i].Value.Trim();
                        if (word != headword && IsValidSynonym(word))
                            synonyms.Add(word);
                    }
                }
            }
        }

        return synonyms.Count > 0 ? synonyms.ToList() : null;
    }

    private static bool IsValidSynonym(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2 || word.Length > 30)
            return false;

        // Must contain letters
        if (!Regex.IsMatch(word, @"[A-Za-z]"))
            return false;

        // Reject common false positives
        var falsePositives = new[] { "see", "cf", "compare", "also", "known", "as", "called", "or", "and" };
        if (falsePositives.Contains(word.ToLowerInvariant()))
            return false;

        return true;
    }

    private static string ExtractDefinitionFromRawFragment(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        // Try to extract meaningful English text from raw fragment
        var lines = rawFragment.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var candidateLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip lines that are clearly not definitions
            if (trimmed.Length < 5 ||
                trimmed.StartsWith("»") ||
                trimmed.Contains("【") ||
                Regex.IsMatch(trimmed, @"^\d+\.\s*$"))
                continue;

            // Check if line contains reasonable English
            var wordCount = Regex.Matches(trimmed, @"\b[a-zA-Z]{3,}\b").Count;
            if (wordCount >= 1)
            {
                candidateLines.Add(trimmed);
            }
        }

        var definition = string.Join(" ", candidateLines).Trim();

        // Clean it
        definition = Regex.Replace(definition, @"[•▶»◘›♦\-]", " ");
        definition = Regex.Replace(definition, @"\s+", " ").Trim();

        return definition;
    }

    private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
    {
        string definition = string.Empty;

        // Try to get definition from RawFragment
        if (!string.IsNullOrWhiteSpace(entry?.RawFragmentLine))
        {
            definition = ExtractDefinitionFromRawFragment(entry.RawFragmentLine);
        }

        // Ensure sense number is at least 1
        var senseNumber = entry?.SenseNumber > 0 ? entry.SenseNumber : 1;

        return new ParsedDefinition
        {
            MeaningTitle = entry?.Word ?? "unnamed sense",
            Definition = definition,
            RawFragment = entry?.RawFragmentLine ?? string.Empty,
            SenseNumber = senseNumber,
            Domain = null,
            UsageLabel = entry?.PartOfSpeech,
            CrossReferences = new List<CrossReference>(),
            Synonyms = new List<string>(),
            Alias = null,
            SourceCode = entry?.SourceCode
        };
    }
}