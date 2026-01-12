// IDictionaryEntrySynonymWriter.cs

namespace DictionaryImporter.Core.Persistence;

public interface IDictionaryEntrySynonymWriter
{
    Task WriteAsync(DictionaryEntrySynonym synonym, CancellationToken ct);

    Task BulkWriteAsync(IEnumerable<DictionaryEntrySynonym> synonyms, CancellationToken ct);

    Task WriteSynonymsForParsedDefinition(long parsedDefinitionId, IEnumerable<string> synonyms, string sourceCode,
        CancellationToken ct);
}