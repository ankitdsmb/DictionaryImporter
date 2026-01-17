using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford
{
    public sealed class OxfordTransformer : IDataTransformer<OxfordRawEntry>
    {
        public IEnumerable<DictionaryEntry> Transform(OxfordRawEntry raw)
        {
            if (raw == null || !raw.Senses.Any())
                yield break;

            foreach (var sense in raw.Senses)
            {
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

            if (!string.IsNullOrEmpty(entry.Pronunciation))
                parts.Add($"【Pronunciation】{entry.Pronunciation}");

            if (!string.IsNullOrEmpty(entry.VariantForms))
                parts.Add($"【Variants】{entry.VariantForms}");

            if (!string.IsNullOrEmpty(sense.SenseLabel))
                parts.Add($"【Label】{sense.SenseLabel}");

            parts.Add(sense.Definition);

            if (!string.IsNullOrEmpty(sense.ChineseTranslation))
                parts.Add($"【Chinese】{sense.ChineseTranslation}");

            if (sense.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in sense.Examples)
                    parts.Add($"» {example}");
            }

            if (!string.IsNullOrEmpty(sense.UsageNote))
                parts.Add($"【Usage】{sense.UsageNote}");

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

            word = word.Replace("★", "")
                .Replace("☆", "")
                .Replace("●", "")
                .Replace("○", "")
                .Replace("▶", "")
                .Trim();

            word = word.ToLowerInvariant();

            word = Regex.Replace(word, @"[^\p{L}\-']", " ");

            word = Regex.Replace(word, @"\s+", " ").Trim();

            return word;
        }
    }
}