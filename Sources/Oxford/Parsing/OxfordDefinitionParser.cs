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
            // CRITICAL FIX: Use RawFragment for definition if Definition is empty or malformed
            var definitionToParse = !string.IsNullOrWhiteSpace(entry.Definition) &&
                                   !entry.Definition.StartsWith("【Label】") &&
                                   entry.Definition.Length > 10
                ? entry.Definition
                : !string.IsNullOrWhiteSpace(entry.RawFragment)
                    ? entry.RawFragment
                    : entry.Definition;

            var (cleanDefinition, domain, usageLabel) =
                ExtractDefinitionAndMetadata(definitionToParse);

            // If clean definition is still empty or too short, try to extract from RawFragment
            if (string.IsNullOrWhiteSpace(cleanDefinition) || cleanDefinition.Length < 5)
            {
                cleanDefinition = ExtractMainDefinitionFromText(entry.RawFragment ?? entry.Definition);
            }

            var examples = ExtractExamples(entry.Definition);
            var crossRefs = ExtractCrossReferences(cleanDefinition);
            var synonyms = ExtractSynonymsFromExamples(examples);

            // Ensure we have a proper MeaningTitle (use the word)
            var meaningTitle = !string.IsNullOrWhiteSpace(entry.Word)
                ? entry.Word
                : "unnamed sense";

            // Ensure SenseNumber is properly set (minimum 1)
            var senseNumber = entry.SenseNumber > 0 ? entry.SenseNumber : 1;

            result = new ParsedDefinition
            {
                MeaningTitle = meaningTitle,
                Definition = cleanDefinition ?? string.Empty,
                RawFragment = entry.RawFragment,
                SenseNumber = senseNumber,
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

        var lines = definition.Split('\n');
        var definitionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("【Label】"))
            {
                var labelContent = trimmed["【Label】".Length..].Trim();

                // Parse domain and usage from label
                var labelParts = labelContent.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in labelParts)
                {
                    var cleanPart = part.Trim().Trim('[', ']');
                    if (!string.IsNullOrWhiteSpace(cleanPart))
                    {
                        // Check if it's a usage label
                        var lowerPart = cleanPart.ToLowerInvariant();
                        if (lowerPart == "informal" || lowerPart == "formal" || lowerPart == "dated" ||
                            lowerPart == "archaic" || lowerPart == "slang" || lowerPart == "humorous" ||
                            lowerPart == "literary" || lowerPart == "technical")
                        {
                            usageLabel = cleanPart;
                        }
                        else
                        {
                            // Assume it's a domain
                            domain = cleanPart;
                        }
                    }
                }
            }
            else if (trimmed.StartsWith("【Examples】"))
            {
                // Stop at examples section
                break;
            }
            else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("»"))
            {
                // This is part of the main definition
                definitionLines.Add(trimmed);
            }
        }

        var cleanDefinition = string.Join(" ", definitionLines).Trim();

        // Clean up any remaining markers
        cleanDefinition = Regex.Replace(cleanDefinition, @"【[^】]*】", "");
        cleanDefinition = Regex.Replace(cleanDefinition, @"\s+", " ").Trim();

        return (cleanDefinition, domain, usageLabel);
    }

    private static string ExtractMainDefinitionFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove section markers
        var cleaned = Regex.Replace(text, @"【[^】]*】", " ");

        // Remove example markers
        cleaned = Regex.Replace(cleaned, @"^»\s*", "", RegexOptions.Multiline);

        // Remove Chinese characters if any remain
        cleaned = Regex.Replace(cleaned, @"[\u4e00-\u9fff]", "");

        // Remove formatting artifacts
        cleaned = Regex.Replace(cleaned, @"[▶»◘›♦•\-]", " ");

        // Clean up
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        // Extract first sentence or meaningful chunk
        var sentences = Regex.Split(cleaned, @"[.!?]+");
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length > 10 && ContainsEnglishWords(trimmed))
            {
                return trimmed + ".";
            }
        }

        // Fallback: return first 100 chars
        return cleaned.Length > 100 ? cleaned.Substring(0, 100).Trim() + "..." : cleaned;
    }

    private static bool ContainsEnglishWords(string text)
    {
        return Regex.IsMatch(text, @"\b[a-zA-Z]{3,}\b");
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
        // Try to extract definition from RawFragment
        string definition = string.Empty;
        if (!string.IsNullOrWhiteSpace(entry?.RawFragment))
        {
            definition = ExtractMainDefinitionFromText(entry.RawFragment);
        }
        else if (!string.IsNullOrWhiteSpace(entry?.Definition))
        {
            definition = ExtractMainDefinitionFromText(entry.Definition);
        }

        // Ensure SenseNumber is at least 1
        var senseNumber = entry?.SenseNumber > 0 ? entry.SenseNumber : 1;

        return new ParsedDefinition
        {
            MeaningTitle = entry?.Word ?? "unnamed sense",
            Definition = definition,
            RawFragment = entry?.RawFragment ?? string.Empty,
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