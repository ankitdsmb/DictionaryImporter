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