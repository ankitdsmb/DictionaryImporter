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

            result = new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = cleanDefinition,
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

        var lines = definition.Split('\n');
        var definitionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("【Label】"))
            {
                var labelContent = trimmed["【Label】".Length..].Trim();

                var commaIndex = labelContent.IndexOf(',');
                if (commaIndex > 0)
                {
                    domain = labelContent[..commaIndex].Trim();

                    var rest = labelContent[(commaIndex + 1)..].Trim();
                    var usageMatch = Regex.Match(rest, @"\[([^\]]+)\]");
                    if (usageMatch.Success)
                        usageLabel = usageMatch.Groups[1].Value.Trim();
                }
                else
                {
                    domain = labelContent;
                }
            }
            else if (trimmed.StartsWith("【Examples】"))
            {
                break;
            }
            else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("»"))
            {
                definitionLines.Add(trimmed);
            }
        }

        var cleanDefinition = string.Join(" ", definitionLines).Trim();
        cleanDefinition = Regex.Replace(cleanDefinition, @"【[^】]*】", "");
        cleanDefinition = Regex.Replace(cleanDefinition, @"\s+", " ").Trim();

        return (cleanDefinition, domain, usageLabel);
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
        return new ParsedDefinition
        {
            MeaningTitle = entry?.Word ?? "unnamed sense",
            Definition = entry?.Definition ?? string.Empty,
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