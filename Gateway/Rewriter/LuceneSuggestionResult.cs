namespace DictionaryImporter.Gateway.Rewriter;

public sealed class LuceneSuggestionResult
{
    public LuceneSuggestionMode Mode { get; init; }

    public string SuggestionText { get; init; } = string.Empty;

    public double Score { get; init; }

    public string MatchedHash { get; init; } = string.Empty;

    public string MatchedOriginalPreview { get; init; } = string.Empty;

    public string Source { get; init; } = "lucene-memory";

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}