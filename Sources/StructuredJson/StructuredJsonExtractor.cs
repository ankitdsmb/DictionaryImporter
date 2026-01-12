using System.Text.Json;

namespace DictionaryImporter.Sources.StructuredJson;

public sealed class StructuredJsonExtractor
    : IDataExtractor<StructuredJsonRawEntry>
{
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

            var entry = kvp.Value;

            foreach (var def in entry.Definitions)
                yield return new StructuredJsonRawEntry
                {
                    Word = entry.OriginalCasedWord,
                    NormalizedWord = entry.TransliteratedWord,
                    Definition = def.Definition,
                    PartOfSpeech = def.PartOfSpeech,
                    SenseNumber = def.Sequence
                };
        }
    }
}