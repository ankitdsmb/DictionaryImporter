namespace DictionaryImporter.Core.Grammar.Enhanced;

public sealed record EngineContribution(
    string EngineName,
    int IssuesFound,
    TimeSpan ProcessingTime,
    bool WasPrimary,
    double ConfidenceWeight
);