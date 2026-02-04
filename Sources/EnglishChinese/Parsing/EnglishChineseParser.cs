using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Sources.EnglishChinese.Parsing;

public sealed class EnglishChineseParser(ILogger<EnglishChineseParser>? logger = null)
    : ISourceDictionaryDefinitionParser
{
    public string SourceCode => "ENG_CHN";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        logger?.LogDebug(
            "EnglishChineseEnhancedParser.Parse called | Word={Word} | RawFragmentPreview={RawFragmentPreview}",
            entry?.Word ?? "null",
            entry?.RawFragmentLine?.Substring(0, Math.Min(50, entry.RawFragmentLine.Length)) ?? "null");

        if (entry == null)
        {
            logger?.LogWarning("Parser received null entry");
            if (entry != null) yield return CreateFallbackParsedDefinition(entry);
            yield break;
        }

        var rawLine = !string.IsNullOrWhiteSpace(entry.RawFragmentLine)
            ? entry.RawFragmentLine
            : entry.Definition;

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            logger?.LogDebug("Empty raw line for entry: {Word}", entry.Word);
            yield return CreateFallbackParsedDefinition(entry);
            yield break;
        }

        var parsedData = ParsingHelperEnglishChinese.ParseEngChnEntry(rawLine);

        var mainSense = CreateParsedDefinition(parsedData, entry, rawLine);
        if (mainSense != null)
        {
            yield return mainSense;
        }

        if (parsedData.AdditionalSenses.Count > 0)
        {
            var senseNumber = entry.SenseNumber + 1;
            foreach (var additionalSense in parsedData.AdditionalSenses)
            {
                var sense = CreateParsedDefinition(additionalSense, entry, rawLine, senseNumber++);
                if (sense != null)
                {
                    yield return sense;
                }
            }
        }
    }

    private ParsedDefinition? CreateParsedDefinition(
        EnglishChineseParsedData data,
        DictionaryEntry entry,
        string rawFragment,
        int senseNumber = 1)
    {
        var fullDefinition = BuildFullDefinition(data);

        if (string.IsNullOrWhiteSpace(data.MainDefinition))
        {
            return null;
        }

        var domain = ParsingHelperEnglishChinese.ExtractDomain(rawFragment);

        var usageLabel = ParsingHelperEnglishChinese.ExtractUsageLabel(rawFragment);

        var parsed = new ParsedDefinition
        {
            MeaningTitle = data.Headword ?? entry.Word ?? "unnamed sense",
            Definition = fullDefinition,
            RawFragment = rawFragment,
            SenseNumber = senseNumber,
            Domain = data.PartOfSpeech != null && Helper.IsPureEnglish(data.PartOfSpeech) ? domain : "",
            UsageLabel = data.PartOfSpeech != null && Helper.IsPureEnglish(data.PartOfSpeech) ? usageLabel : "",
            CrossReferences = new List<CrossReference>(),
            Synonyms = null,
            Alias = null
        };

        if (data.Examples.Count > 0)
        {
            parsed.Examples = data.Examples.Where(Helper.IsPureEnglish).ToList();
        }

        logger?.LogDebug(
            "Created ParsedDefinition | Word={Word} | Domain={Domain} | POS={POS} | DefLength={Length}",
            parsed.MeaningTitle, domain, data.PartOfSpeech, parsed.Definition.Length);

        return parsed;
    }

    private string BuildFullDefinition(EnglishChineseParsedData data)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(data.Syllabification))
            parts.Add($"Syllabification: {data.Syllabification}");

        if (!string.IsNullOrWhiteSpace(data.IpaPronunciation))
            parts.Add($"Pronunciation: /{data.IpaPronunciation}/");

        if (!string.IsNullOrWhiteSpace(data.PartOfSpeech) && Helper.IsPureEnglish(data.PartOfSpeech))
            parts.Add($"POS: {data.PartOfSpeech}");

        parts.Add(data.EnglishDefinition);

        if (!string.IsNullOrWhiteSpace(data.Etymology) && Helper.IsPureEnglish(data.Etymology))
            parts.Add($"Etymology: {data.Etymology}");

        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
    }

    private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
    {
        logger?.LogWarning("Creating fallback ParsedDefinition for: {Word}", entry?.Word);
        return new ParsedDefinition
        {
            MeaningTitle = entry?.Word ?? "unnamed sense",
            Definition = entry?.Definition ?? string.Empty,
            RawFragment = entry?.RawFragmentLine ?? entry?.Definition ?? string.Empty,
            SenseNumber = entry?.SenseNumber ?? 1,
            Domain = null,
            UsageLabel = null,
            CrossReferences = new List<CrossReference>(),
            Synonyms = null,
            Alias = null
        };
    }
}