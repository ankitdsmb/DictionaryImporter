namespace DictionaryImporter.AITextKit.Grammar.Enhanced;

public sealed record GrammarPipelineDiagnostics(
    IReadOnlyDictionary<string, EngineContribution> EngineContributions,
    TimeSpan TotalProcessingTime,
    int TotalIssuesFound,
    Dictionary<string, int> IssuesByCategory
);