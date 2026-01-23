using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.StructuredJson
{
    public sealed class StructuredJsonTransformer(ILogger<StructuredJsonTransformer> logger)
        : IDataTransformer<StructuredJsonRawEntry>
    {
        private const string SourceCode = "STRUCT_JSON";

        public IEnumerable<DictionaryEntry> Transform(StructuredJsonRawEntry raw)
        {
            if (raw == null)
                yield break;

            foreach (var entry in ProcessStructuredJsonEntry(raw))
            {
                yield return entry;
            }
        }

        private IEnumerable<DictionaryEntry> ProcessStructuredJsonEntry(StructuredJsonRawEntry raw)
        {
            var entries = new List<DictionaryEntry>();

            try
            {
                var word = raw.Word?.Trim();
                if (string.IsNullOrWhiteSpace(word))
                    yield break;

                var normalizedWord = raw.NormalizedWord;
                if (string.IsNullOrWhiteSpace(normalizedWord))
                    normalizedWord = Helper.NormalizeWord(word);

                entries.Add(new DictionaryEntry
                {
                    SourceCode = SourceCode,
                    Word = word,
                    NormalizedWord = normalizedWord,
                    Definition = raw.Definition,
                    RawFragment = raw.Definition, // safe fallback
                    PartOfSpeech = Helper.NormalizePartOfSpeech(raw.PartOfSpeech),
                    SenseNumber = raw.SenseNumber > 0 ? raw.SenseNumber : 1,
                    CreatedUtc = DateTime.UtcNow
                });

                Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                Helper.HandleError(logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
            {
                yield return entry;
            }
        }
    }
}