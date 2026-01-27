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

                // 2. Build full definition with proper section ordering
                var fullDefinition = BuildFullDefinition(raw, sense);

                // 3. Create entry
                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = resolvedPos,
                    Definition = fullDefinition,
                    RawFragment = sense.Definition, // Keep sense-level raw fragment
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
        // Priority 1: Sense-level POS block (▶ adjective, noun, etc.)
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(sense.SenseLabel);
            if (normalized != "unk")
                return normalized;
        }

        // Priority 2: Check if sense definition starts with POS in parentheses
        if (!string.IsNullOrWhiteSpace(sense.Definition))
        {
            var firstParenMatch = Regex.Match(sense.Definition, @"^\(([^)]+)\)");
            if (firstParenMatch.Success)
            {
                var possiblePos = firstParenMatch.Groups[1].Value.Trim();
                var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(possiblePos);
                if (normalized != "unk")
                    return normalized;
            }
        }

        // Priority 3: Headword-level POS
        if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
        {
            var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech);
            if (normalized != "unk")
                return normalized;
        }

        // Default: unknown
        return "unk";
    }

    private static string BuildFullDefinition(OxfordRawEntry entry, OxfordSenseRaw sense)
    {
        var parts = new List<string>();

        // Pronunciation (if available at entry level)
        if (!string.IsNullOrWhiteSpace(entry.Pronunciation))
            parts.Add($"【Pronunciation】{entry.Pronunciation}");

        // Variants (if available at entry level)
        if (!string.IsNullOrWhiteSpace(entry.VariantForms))
            parts.Add($"【Variants】{entry.VariantForms}");

        // Sense label (domain/register information)
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            // Don't add if it's just a POS that we already extracted
            var normalizedPos = OxfordSourceDataHelper.NormalizePartOfSpeech(sense.SenseLabel);
            if (normalizedPos == "unk")
                parts.Add($"【Label】{sense.SenseLabel}");
        }

        // Main definition
        var mainDefinition = sense.Definition ?? string.Empty;
        parts.Add(mainDefinition);

        // Chinese translation (if not already included in main definition)
        if (!string.IsNullOrWhiteSpace(sense.ChineseTranslation) &&
            !mainDefinition.Contains(sense.ChineseTranslation) &&
            !mainDefinition.Contains("•"))
        {
            parts.Add($"• {sense.ChineseTranslation}");
        }

        // Examples
        if (sense.Examples != null && sense.Examples.Count > 0)
        {
            parts.Add("【Examples】");
            foreach (var example in sense.Examples)
                parts.Add($"» {example}");
        }

        // Usage note
        if (!string.IsNullOrWhiteSpace(sense.UsageNote))
        {
            if (sense.UsageNote.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
                parts.Add($"【Usage】{sense.UsageNote.Substring(6).Trim()}");
            else if (sense.UsageNote.StartsWith("Grammar:", StringComparison.OrdinalIgnoreCase))
                parts.Add($"【Grammar】{sense.UsageNote.Substring(8).Trim()}");
            else if (sense.UsageNote.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
                parts.Add($"【Note】{sense.UsageNote.Substring(5).Trim()}");
            else
                parts.Add($"【Usage】{sense.UsageNote}");
        }

        // Cross-references
        if (sense.CrossReferences != null && sense.CrossReferences.Count > 0)
        {
            parts.Add($"【SeeAlso】{string.Join("; ", sense.CrossReferences)}");
        }

        return string.Join("\n", parts);
    }
}