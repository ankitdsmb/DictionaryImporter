namespace DictionaryImporter.Gateway.Rewriter;

public interface ILuceneSuggestionEngine
{
    Task<IReadOnlyList<LuceneSuggestionResult>> GetSuggestionsAsync(
        string sourceCode,
        LuceneSuggestionMode mode,
        string inputText,
        int maxSuggestions,
        double minScore,
        CancellationToken cancellationToken);
}