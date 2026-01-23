using DictionaryImporter.Common;

namespace DictionaryImporter.Sources.StructuredJson
{
    public sealed class StructuredJsonExtractor
        : IDataExtractor<StructuredJsonRawEntry>
    {
        private const string SourceCode = "STRUCT_JSON";

        public async IAsyncEnumerable<StructuredJsonRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var data =
                await JsonSerializer.DeserializeAsync<
                    Dictionary<string, StructuredJsonEntry>>(
                    stream,
                    cancellationToken: ct)
                ?? throw new InvalidOperationException(
                    "Failed to deserialize structured JSON dictionary");

            foreach (var kvp in data)
            {
                ct.ThrowIfCancellationRequested();

                // ✅ early stop (before processing more keys)
                if (!Helper.ShouldContinueProcessing(SourceCode, null))
                    yield break;

                var entry = kvp.Value;
                if (entry == null)
                    continue;

                var word = entry.OriginalCasedWord;
                var normalizedWord = entry.TransliteratedWord;

                if (string.IsNullOrWhiteSpace(word))
                    continue;

                if (string.IsNullOrWhiteSpace(normalizedWord))
                    normalizedWord = Helper.NormalizeWord(word);

                if (entry.Definitions == null || entry.Definitions.Count == 0)
                    continue;

                foreach (var def in entry.Definitions)
                {
                    ct.ThrowIfCancellationRequested();

                    // ✅ keep existing strict stop here too (counts yielded senses accurately)
                    if (!Helper.ShouldContinueProcessing(SourceCode, null))
                        yield break;

                    if (def == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(def.Definition))
                        continue;

                    var senseNumber = def.Sequence > 0 ? def.Sequence : 1;

                    yield return new StructuredJsonRawEntry
                    {
                        Word = word,
                        NormalizedWord = normalizedWord,
                        Definition = def.Definition,
                        PartOfSpeech = def.PartOfSpeech,
                        SenseNumber = senseNumber
                    };
                }
            }
        }
    }
}