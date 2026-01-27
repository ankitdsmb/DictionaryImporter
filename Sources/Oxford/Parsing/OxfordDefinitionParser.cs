using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Sources.Oxford.Parsing;

public sealed class OxfordDefinitionParser(ILogger<OxfordDefinitionParser> logger)
    : ISourceDictionaryDefinitionParser
{
    private readonly ILogger<OxfordDefinitionParser> _logger = logger;

    public string SourceCode => "ENG_OXFORD";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        ParsedDefinition result;

        if (string.IsNullOrWhiteSpace(entry?.Definition))
        {
            result = CreateFallbackParsedDefinition(entry);
            return new[] { result };
        }

        try
        {
            var (cleanDefinition, domain, usageLabel) =
                ExtractDefinitionAndMetadata(entry.Definition);

            var examples = ExtractExamples(entry.Definition);
            var crossRefs = ExtractCrossReferences(cleanDefinition);
            var synonyms = ExtractSynonymsFromExamples(examples);

            // CRITICAL FIX: If cleanDefinition is empty, try to extract from RawFragment
            if (string.IsNullOrWhiteSpace(cleanDefinition) && !string.IsNullOrWhiteSpace(entry.RawFragment))
            {
                cleanDefinition = ExtractMainDefinitionFromRawFragment(entry.RawFragment);
            }

            // If still empty, use a fallback
            if (string.IsNullOrWhiteSpace(cleanDefinition))
            {
                cleanDefinition = ExtractAnyEnglishText(entry.Definition);
            }

            result = new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = cleanDefinition ?? string.Empty,
                RawFragment = entry.RawFragment,
                SenseNumber = entry.SenseNumber,
                Domain = domain,
                UsageLabel = usageLabel ?? entry.PartOfSpeech,
                CrossReferences = crossRefs,
                Synonyms = synonyms,
                Alias = null,
                SourceCode = entry.SourceCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Oxford definition for {Word}", entry?.Word);
            result = CreateFallbackParsedDefinition(entry);
        }

        return new[] { result };
    }

    private static (string cleanDefinition, string? domain, string? usageLabel)
        ExtractDefinitionAndMetadata(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return (string.Empty, null, null);

        string? domain = null;
        string? usageLabel = null;
        string? extractedDomain = null;

        var lines = definition.Split('\n');
        var definitionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("【Label】"))
            {
                var labelContent = trimmed["【Label】".Length..].Trim();

                // Extract domain and usage from label
                var commaIndex = labelContent.IndexOf(',');
                if (commaIndex > 0)
                {
                    extractedDomain = labelContent[..commaIndex].Trim();

                    var rest = labelContent[(commaIndex + 1)..].Trim();
                    var usageMatch = Regex.Match(rest, @"\[([^\]]+)\]");
                    if (usageMatch.Success)
                        usageLabel = usageMatch.Groups[1].Value.Trim();
                }
                else
                {
                    extractedDomain = labelContent;
                }

                // Clean domain text
                if (!string.IsNullOrWhiteSpace(extractedDomain))
                {
                    extractedDomain = Regex.Replace(extractedDomain, @"▶\s*", "").Trim();
                    domain = extractedDomain;
                }
            }
            else if (trimmed.StartsWith("【Examples】"))
            {
                // Stop collecting definition lines at Examples section
                break;
            }
            else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("»"))
            {
                // This is part of the definition
                definitionLines.Add(trimmed);
            }
        }

        var cleanDefinition = string.Join(" ", definitionLines).Trim();

        // Clean up any remaining section markers
        cleanDefinition = Regex.Replace(cleanDefinition, @"【[^】]*】", "");
        cleanDefinition = Regex.Replace(cleanDefinition, @"\s+", " ").Trim();

        // If domain wasn't extracted from Label, try to extract from definition text
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = ExtractDomainFromDefinitionText(cleanDefinition);
        }

        return (cleanDefinition, domain, usageLabel);
    }

    private static string? ExtractDomainFromDefinitionText(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // Look for common domain/usage labels in brackets
        var bracketMatch = Regex.Match(definition, @"\[([^\]]+)\]");
        if (bracketMatch.Success)
        {
            var label = bracketMatch.Groups[1].Value.Trim().ToLowerInvariant();

            // Common Oxford usage labels
            if (label.Contains("informal")) return "informal";
            if (label.Contains("formal")) return "formal";
            if (label.Contains("dated")) return "dated";
            if (label.Contains("archaic")) return "archaic";
            if (label.Contains("slang")) return "slang";
            if (label.Contains("technical")) return "technical";
            if (label.Contains("literary")) return "literary";
            if (label.Contains("humorous")) return "humorous";
            if (label.Contains("n. amer.") || label.Contains("north american")) return "N. Amer.";
            if (label.Contains("british")) return "British";
            if (label.Contains("chiefly")) return "chiefly";
            if (label.Contains("especially")) return "especially";
        }

        return null;
    }

    private static string ExtractMainDefinitionFromRawFragment(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var lines = rawFragment.Split('\n');
        var definitionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip lines that are clearly not definition text
            if (trimmed.StartsWith("【") || trimmed.StartsWith("»") ||
                string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.Contains("•") && Regex.IsMatch(trimmed, @"•\s*[\u4e00-\u9fff]"))
            {
                continue;
            }

            // Remove any remaining Chinese characters
            var cleanedLine = Regex.Replace(trimmed, @"[\u4e00-\u9fff]", "");
            cleanedLine = Regex.Replace(cleanedLine, @"•.*", "").Trim();

            if (!string.IsNullOrWhiteSpace(cleanedLine))
            {
                definitionLines.Add(cleanedLine);
            }
        }

        return string.Join(" ", definitionLines).Trim();
    }

    private static string ExtractAnyEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Extract any English text by removing markers and non-English content
        var cleaned = text;

        // Remove section markers
        cleaned = Regex.Replace(cleaned, @"【[^】]*】", " ");

        // Remove example markers
        cleaned = Regex.Replace(cleaned, @"^»\s*", "", RegexOptions.Multiline);

        // Remove Chinese characters and their markers
        cleaned = Regex.Replace(cleaned, @"[\u4e00-\u9fff]", "");
        cleaned = Regex.Replace(cleaned, @"•.*", "");

        // Clean up
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    private static IReadOnlyList<string> ExtractExamples(string definition)
    {
        var examples = new List<string>();
        if (string.IsNullOrWhiteSpace(definition))
            return examples;

        var lines = definition.Split('\n');
        var inExamplesSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("【Examples】"))
            {
                inExamplesSection = true;
                continue;
            }

            if (!inExamplesSection)
                continue;

            if (trimmed.StartsWith("【"))
                break;

            if (trimmed.StartsWith("»"))
            {
                var example = trimmed[1..].Trim();
                if (!string.IsNullOrWhiteSpace(example))
                    examples.Add(example);
            }
        }

        return examples;
    }

    private static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
    {
        var crossRefs = new List<CrossReference>();

        if (string.IsNullOrWhiteSpace(definition))
            return crossRefs;

        var seeMatches = Regex.Matches(
            definition,
            @"--›\s*(?:see|cf\.?|compare)\s+([A-Za-z\-']+)"
        );

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

        var variantMatches = Regex.Matches(
            definition,
            @"(?:variant of|another term for|同)\s+([A-Za-z\-']+)"
        );

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

    private static IReadOnlyList<string>? ExtractSynonymsFromExamples(
        IReadOnlyList<string> examples)
    {
        if (examples == null || examples.Count == 0)
            return null;

        var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var example in examples)
        {
            var orMatch = Regex.Match(
                example,
                @"\b([A-Za-z\-']+)\s+or\s+([A-Za-z\-']+)\b"
            );

            if (orMatch.Success)
            {
                synonyms.Add(orMatch.Groups[1].Value);
                synonyms.Add(orMatch.Groups[2].Value);
            }
        }

        return synonyms.Count > 0 ? synonyms.ToList() : null;
    }

    private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
    {
        // Try to extract some definition even in fallback
        string definition = string.Empty;
        if (!string.IsNullOrWhiteSpace(entry?.Definition))
        {
            definition = ExtractAnyEnglishText(entry.Definition);
        }

        if (string.IsNullOrWhiteSpace(definition) && !string.IsNullOrWhiteSpace(entry?.RawFragment))
        {
            definition = ExtractMainDefinitionFromRawFragment(entry.RawFragment);
        }

        return new ParsedDefinition
        {
            MeaningTitle = entry?.Word ?? "unnamed sense",
            Definition = definition ?? string.Empty,
            RawFragment = entry?.RawFragment ?? string.Empty,
            SenseNumber = entry?.SenseNumber ?? 1,
            Domain = null,
            UsageLabel = entry?.PartOfSpeech,
            CrossReferences = new List<CrossReference>(),
            Synonyms = new List<string>(),
            Alias = null,
            SourceCode = entry?.SourceCode
        };
    }
}