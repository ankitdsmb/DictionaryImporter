using DictionaryImporter.Common;
using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordTransformer(ILogger<OxfordTransformer> logger)
    : IDataTransformer<OxfordRawEntry>
{
    private const string SourceCode = "ENG_OXFORD";

    public IEnumerable<DictionaryEntry> Transform(OxfordRawEntry? raw)
    {
        if (raw == null || raw.Senses == null || raw.Senses.Count == 0)
            yield break;

        foreach (var entry in ProcessOxfordEntry(raw))
        {
            // apply limit per produced DictionaryEntry
            if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                yield break;

            yield return entry;
        }
    }

    private IEnumerable<DictionaryEntry> ProcessOxfordEntry(OxfordRawEntry raw)
    {
        var entries = new List<DictionaryEntry>();

        try
        {
            var normalizedWord =
                Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

            foreach (var sense in raw.Senses)
            {
                // 1. POS resolution (Oxford-correct)
                var resolvedPos = ResolvePartOfSpeech(raw, sense);

                var fullDefinition = BuildFullDefinition(raw, sense);

                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = resolvedPos,
                    Definition = fullDefinition,

                    // keep truly raw (no headers, no examples)
                    RawFragment = sense.Definition,

                    SenseNumber = sense.SenseNumber,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow
                });
            }

            Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));
        }
        catch (Exception ex)
        {
            Helper.HandleError(logger, ex, SourceCode, "transforming");
        }

        foreach (var entry in entries)
            yield return entry;
    }

    private static string ResolvePartOfSpeech(OxfordRawEntry raw, OxfordSenseRaw sense)
    {
        // Prefer POS coming from Oxford POS block (▶ adjective, noun, etc.)
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            var normalized =
                OxfordSourceDataHelper.NormalizePartOfSpeech(sense.SenseLabel);

            if (normalized != "unk")
                return normalized;
        }

        // Fallback to headword POS
        return OxfordSourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech);
    }

    private static string BuildFullDefinition(OxfordRawEntry entry, OxfordSenseRaw sense)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.Pronunciation))
            parts.Add($"【Pronunciation】{entry.Pronunciation}");

        if (!string.IsNullOrWhiteSpace(entry.VariantForms))
            parts.Add($"【Variants】{entry.VariantForms}");

        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
            parts.Add($"【Label】{sense.SenseLabel}");

        // Main definition (English + Chinese, as Oxford provides)
        parts.Add(sense.Definition);

        // Safety: only add Chinese if extractor didn't already embed it
        if (!string.IsNullOrWhiteSpace(sense.ChineseTranslation) &&
            !sense.Definition.Contains(sense.ChineseTranslation))
        {
            parts.Add($"【Chinese】{sense.ChineseTranslation}");
        }

        if (sense.Examples != null && sense.Examples.Count > 0)
        {
            parts.Add("【Examples】");
            foreach (var example in sense.Examples)
                parts.Add($"» {example}");
        }

        if (!string.IsNullOrWhiteSpace(sense.UsageNote))
            parts.Add($"【Usage】{sense.UsageNote}");

        if (sense.CrossReferences != null && sense.CrossReferences.Count > 0)
        {
            parts.Add("【SeeAlso】");
            parts.Add(string.Join("; ", sense.CrossReferences));
        }

        return string.Join("\n", parts);
    }
}