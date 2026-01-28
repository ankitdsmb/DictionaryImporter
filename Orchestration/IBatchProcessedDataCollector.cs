namespace DictionaryImporter.Orchestration;

public interface IBatchProcessedDataCollector
{
    Task AddParsedDefinitionAsync(long dictionaryEntryId, ParsedDefinition parsed, string sourceCode);

    Task AddExampleAsync(long parsedId, string exampleText, string sourceCode);

    Task AddSynonymsAsync(long parsedId, IEnumerable<string> synonyms, string sourceCode);

    Task AddCrossReferenceAsync(long parsedId, CrossReference crossReference, string sourceCode);

    Task AddAliasAsync(long parsedId, string alias, long dictionaryEntryId, string sourceCode);

    Task AddEtymologyAsync(DictionaryEntryEtymology etymology);

    Task FlushBatchAsync(CancellationToken ct);

    int BatchSize { get; }
}