namespace DictionaryImporter.Gateway.Rewriter;

public sealed class LuceneSuggestionsOptions
{
    public bool Enabled { get; set; } = false;

    public string IndexPath { get; set; } = "indexes/lucene/dictionary-rewrite-memory";

    public int MaxSuggestions { get; set; } = 3;

    public double MinScore { get; set; } = 1.2;

    public int Take { get; set; } = 500;

    // NEW: candidate capture
    public bool WriteCandidatesToSql { get; set; } = false;

    public decimal CandidateMinConfidence { get; set; } = 0.75m;

    public int MaxCandidatesPerRun { get; set; } = 300;
}