using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Sources.Oxford.Models;
using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordTransformer : IDataTransformer<OxfordRawEntry>
{
    public IEnumerable<DictionaryEntry> Transform(OxfordRawEntry raw)
    {
        if (raw == null || !raw.Senses.Any())
            yield break;

        foreach (var sense in raw.Senses)
        {
            // Build full definition with all components
            var fullDefinition = BuildFullDefinition(raw, sense);

            yield return new DictionaryEntry
            {
                Word = raw.Headword,
                NormalizedWord = NormalizeWord(raw.Headword),
                PartOfSpeech = OxfordParserHelper.NormalizePartOfSpeech(raw.PartOfSpeech),
                Definition = fullDefinition,
                SenseNumber = sense.SenseNumber,
                SourceCode = "ENG_OXFORD",
                CreatedUtc = DateTime.UtcNow
            };
        }
    }

    private static string BuildFullDefinition(OxfordRawEntry entry, OxfordSenseRaw sense)
    {
        var parts = new List<string>();

        // Add pronunciation if available
        if (!string.IsNullOrEmpty(entry.Pronunciation))
            parts.Add($"【Pronunciation】{entry.Pronunciation}");

        // Add variant forms if available
        if (!string.IsNullOrEmpty(entry.VariantForms))
            parts.Add($"【Variants】{entry.VariantForms}");

        // Add sense label if available
        if (!string.IsNullOrEmpty(sense.SenseLabel))
            parts.Add($"【Label】{sense.SenseLabel}");

        // Add main definition
        parts.Add(sense.Definition);

        // Add Chinese translation if available
        if (!string.IsNullOrEmpty(sense.ChineseTranslation))
            parts.Add($"【Chinese】{sense.ChineseTranslation}");

        // Add examples
        if (sense.Examples.Any())
        {
            parts.Add("【Examples】");
            foreach (var example in sense.Examples)
                parts.Add($"» {example}");
        }

        // Add usage note if available
        if (!string.IsNullOrEmpty(sense.UsageNote))
            parts.Add($"【Usage】{sense.UsageNote}");

        // Add cross-references if available
        if (sense.CrossReferences.Any())
        {
            parts.Add("【SeeAlso】");
            parts.Add(string.Join("; ", sense.CrossReferences));
        }

        return string.Join("\n", parts);
    }

    private static string NormalizeWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        // Remove any decorative characters
        word = word.Replace("★", "")
            .Replace("☆", "")
            .Replace("●", "")
            .Replace("○", "")
            .Replace("▶", "")
            .Trim();

        // Convert to lowercase
        word = word.ToLowerInvariant();

        // Remove any non-alphabetic characters (keeping hyphens for compound words)
        word = Regex.Replace(word, @"[^\p{L}\-']", " ");

        // Normalize whitespace
        word = Regex.Replace(word, @"\s+", " ").Trim();

        return word;
    }
}