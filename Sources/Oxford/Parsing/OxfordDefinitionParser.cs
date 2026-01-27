using System.Text.RegularExpressions;
using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Sources.Oxford.Parsing;

public sealed class OxfordDefinitionParser(ILogger<OxfordDefinitionParser> logger)
    : ISourceDictionaryDefinitionParser
{
    private readonly ILogger<OxfordDefinitionParser> _logger = logger;

    public string SourceCode => "ENG_OXFORD";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        // must always return exactly 1 parsed definition
        ParsedDefinition result;

        if (string.IsNullOrWhiteSpace(entry?.Definition))
        {
            result = CreateFallbackParsedDefinition(entry);
            return new[] { result };
        }

        try
        {
            var definition = entry.Definition;

            var parsedData = ExtractParsedData(definition);
            var mainDefinition = ExtractMainDefinition(definition);
            var cleanDefinition = CleanDefinitionForOutput(mainDefinition);

            result = new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = cleanDefinition,
                RawFragment = entry.RawFragment,
                SenseNumber = entry.SenseNumber,
                Domain = parsedData.Domain,
                UsageLabel = parsedData.UsageLabel ?? entry.PartOfSpeech,
                CrossReferences = parsedData.CrossReferences ?? new List<CrossReference>(),
                Synonyms = parsedData.Synonyms ?? new List<string>(),
                Alias = parsedData.Alias,
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

    private static ParsedData ExtractParsedData(string definition)
    {
        var data = new ParsedData();

        if (string.IsNullOrWhiteSpace(definition))
            return data;

        var labelMatch = Regex.Match(definition, @"【Label】(.+?)(?:\n|$)");
        if (labelMatch.Success)
        {
            data.Domain = labelMatch.Groups[1].Value.Trim();
        }

        var usageMatch = Regex.Match(definition, @"\[([^\]]+)\]");
        if (usageMatch.Success)
        {
            data.UsageLabel = usageMatch.Groups[1].Value.Trim();
        }

        data.CrossReferences = ExtractCrossReferences(definition);

        return data;
    }

    private static string ExtractMainDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var examplesIndex = definition.IndexOf("【Examples】", StringComparison.Ordinal);

        if (examplesIndex >= 0)
            return definition[..examplesIndex].Trim();

        return definition.Trim();
    }

    private static string CleanDefinitionForOutput(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var text = definition;

        text = Regex.Replace(text, @"【Label】", "");
        text = Regex.Replace(text, @"【[^】]*】", "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = text.TrimEnd('.', ',', ';', ':');

        return text;
    }

    private static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
    {
        var crossRefs = new List<CrossReference>();

        if (string.IsNullOrWhiteSpace(definition))
            return crossRefs;

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

        var variantMatches = Regex.Matches(definition, @"(?:variant of|another term for|同)\s+([A-Za-z\-']+)");
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

        var alsoMatches = Regex.Matches(definition, @"\(also\s+([A-Za-z\-']+)\)");
        foreach (Match match in alsoMatches)
        {
            if (match.Groups[1].Success)
            {
                crossRefs.Add(new CrossReference
                {
                    TargetWord = match.Groups[1].Value.Trim(),
                    ReferenceType = "Also"
                });
            }
        }

        return crossRefs;
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

    private sealed class ParsedData
    {
        public string? Domain { get; set; }
        public string? UsageLabel { get; set; }
        public IReadOnlyList<CrossReference>? CrossReferences { get; set; }
        public IReadOnlyList<string>? Synonyms { get; set; }
        public string? Alias { get; set; }
    }
}