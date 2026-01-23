using DictionaryImporter.Common;

namespace DictionaryImporter.Sources.Collins
{
    public sealed class CollinsTransformer(ILogger<CollinsTransformer> logger)
        : IDataTransformer<CollinsRawEntry>
    {
        private const string SourceCode = "ENG_COLLINS";

        public IEnumerable<DictionaryEntry> Transform(CollinsRawEntry? raw)
        {
            if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                yield break;

            if (raw == null || !raw.Senses.Any())
                yield break;

            foreach (var entry in ProcessCollinsEntry(raw))
                yield return entry;
        }

        private IEnumerable<DictionaryEntry> ProcessCollinsEntry(CollinsRawEntry raw)
        {
            var entries = new List<DictionaryEntry>();

            try
            {
                var normalizedWord = Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

                foreach (var sense in raw.Senses)
                {
                    var fullDefinition = BuildFullDefinition(sense);

                    entries.Add(new DictionaryEntry
                    {
                        Word = raw.Headword,
                        NormalizedWord = normalizedWord,
                        PartOfSpeech = Helper.NormalizePartOfSpeech(sense.PartOfSpeech),
                        Definition = fullDefinition,
                        RawFragment = fullDefinition, // FIX: avoid missing RawFragment warnings
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

        private static string BuildFullDefinition(CollinsSenseRaw sense)
        {
            var parts = new List<string> { sense.Definition };

            if (!string.IsNullOrEmpty(sense.UsageNote))
                parts.Add($"【Note】{sense.UsageNote}");

            if (sense.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in sense.Examples)
                    parts.Add($"• {example}");
            }

            if (!string.IsNullOrEmpty(sense.DomainLabel))
                parts.Add($"【Domain】{sense.DomainLabel}");

            if (!string.IsNullOrEmpty(sense.GrammarInfo))
                parts.Add($"【Grammar】{sense.GrammarInfo}");

            return string.Join("\n", parts);
        }
    }
}