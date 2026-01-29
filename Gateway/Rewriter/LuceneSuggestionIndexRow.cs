namespace DictionaryImporter.Gateway.Rewriter;

public sealed class LuceneSuggestionIndexRow
{
    public string SourceCode { get; init; } = string.Empty;
    public LuceneSuggestionMode Mode { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string EnhancedText { get; init; } = string.Empty;
    public string OriginalTextHash { get; init; } = string.Empty;
}