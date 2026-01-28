namespace DictionaryImporter.Gateway.Rewriter;

public sealed class RewriteMapCandidateUpsert
{
    public string SourceCode { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string FromText { get; init; } = string.Empty;
    public string ToText { get; init; } = string.Empty;
    public decimal Confidence { get; init; } = 0;
}