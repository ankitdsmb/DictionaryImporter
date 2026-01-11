using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Sources.Collins.Models;

namespace DictionaryImporter.Sources.Collins
{
    public sealed class CollinsTransformer : IDataTransformer<CollinsRawEntry>
    {
        public IEnumerable<DictionaryEntry> Transform(CollinsRawEntry raw)
        {
            if (raw == null || !raw.Senses.Any())
                yield break;

            foreach (var sense in raw.Senses)
            {
                // Build combined definition with examples
                var fullDefinition = BuildFullDefinition(sense);

                yield return new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = NormalizeWord(raw.Headword),
                    PartOfSpeech = sense.PartOfSpeech,
                    Definition = fullDefinition,
                    SenseNumber = sense.SenseNumber,
                    SourceCode = "ENG_COLLINS",
                    CreatedUtc = DateTime.UtcNow
                };
            }
        }

        private static string BuildFullDefinition(CollinsSenseRaw sense)
        {
            var parts = new List<string>();

            // Add main definition
            parts.Add(sense.Definition);

            // Add usage note if present
            if (!string.IsNullOrEmpty(sense.UsageNote))
                parts.Add($"【Note】{sense.UsageNote}");

            // Add examples
            if (sense.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in sense.Examples)
                    parts.Add($"• {example}");
            }

            // Add domain/grammar info
            if (!string.IsNullOrEmpty(sense.DomainLabel))
                parts.Add($"【Domain】{sense.DomainLabel}");

            if (!string.IsNullOrEmpty(sense.GrammarInfo))
                parts.Add($"【Grammar】{sense.GrammarInfo}");

            return string.Join("\n", parts);
        }

        private static string NormalizeWord(string word)
        {
            return word.ToLowerInvariant().Trim();
        }
    }
}