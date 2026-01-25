namespace DictionaryImporter.Gateway.Rewriter;

public interface ILuceneSuggestionIndexRepository
{
    Task<IReadOnlyList<LuceneSuggestionIndexRow>> GetRewritePairsAsync(
        string? sourceCode,
        int take,
        int skip,
        CancellationToken cancellationToken);

    // NEW METHOD (added)
    Task<IReadOnlyList<LuceneSuggestionIndexRow>> GetRewritePairsAfterIdAsync(
        string sourceCode,
        long lastParsedDefinitionId,
        int take,
        CancellationToken cancellationToken);
}

public sealed class LuceneSuggestionIndexRow
{
    public string SourceCode { get; init; } = string.Empty;
    public LuceneSuggestionMode Mode { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string EnhancedText { get; init; } = string.Empty;
    public string OriginalTextHash { get; init; } = string.Empty;
}