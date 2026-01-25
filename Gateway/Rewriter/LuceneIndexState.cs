namespace DictionaryImporter.Gateway.Rewriter;

public sealed class LuceneIndexState
{
    public string SourceCode { get; set; } = "UNKNOWN";

    public long LastIndexedParsedDefinitionId { get; set; } = 0;

    public DateTime LastIndexedUtc { get; set; } = DateTime.UtcNow;
}