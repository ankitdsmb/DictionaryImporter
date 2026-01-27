using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
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
        if (string.IsNullOrWhiteSpace(entry.Definition))
        {
            yield return Helper.CreateFallbackParsedDefinition(entry);
            yield break;
        }

        var definition = entry.Definition;

        // Parse all Oxford data at once
        var oxfordData = ParsingHelperOxford.ParseOxfordEntry(definition);
        var examples = ParsingHelperOxford.ExtractExamples(definition);
        var crossRefs = ParsingHelperOxford.ExtractCrossReferences(definition) ?? new List<CrossReference>();
        var synonyms = ParsingHelperOxford.ExtractSynonymsFromExamples(examples);

        // Clean the definition using the extracted domain
        var cleanDefinition = ParsingHelperOxford.CleanOxfordDefinition(
            definition,
            oxfordData.Domain);

        // Build definition with IPA if available
        var fullDefinition = cleanDefinition;
        if (!string.IsNullOrWhiteSpace(oxfordData.IpaPronunciation))
        {
            fullDefinition = $"【Pronunciation】/{oxfordData.IpaPronunciation}/\n{fullDefinition}";
        }

        // Add variants if available
        if (oxfordData.Variants.Count > 0)
        {
            // Only add if not already in the cleaned definition
            if (!fullDefinition.Contains("【Variants】"))
                fullDefinition += $"\n【Variants】{string.Join(", ", oxfordData.Variants)}";
        }

        // Add usage label if available
        if (!string.IsNullOrWhiteSpace(oxfordData.UsageLabel))
        {
            if (!fullDefinition.Contains("【Usage】") && !fullDefinition.Contains("【Grammar】"))
                fullDefinition += $"\n【Usage】{oxfordData.UsageLabel}";
        }

        // Determine usage label for ParsedDefinition
        var usageLabelForOutput = oxfordData.UsageLabel;
        if (string.IsNullOrWhiteSpace(usageLabelForOutput))
            usageLabelForOutput = entry.PartOfSpeech;

        yield return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = fullDefinition.Trim(),
            RawFragment = entry.Definition,
            SenseNumber = entry.SenseNumber,
            Domain = oxfordData.Domain,
            UsageLabel = usageLabelForOutput,
            CrossReferences = crossRefs,
            Synonyms = synonyms,
            Alias = oxfordData.Variants.FirstOrDefault()
        };
    }
}