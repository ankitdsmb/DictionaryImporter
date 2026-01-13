// File: DictionaryImporter.Core/Grammar/Enhanced/IGrammarPipeline.cs
using System.Collections.Concurrent;

namespace DictionaryImporter.Core.Grammar.Enhanced;

public interface IGrammarPipeline : IGrammarCorrector
{
    /// <summary>
    /// Diagnostic information about which engines contributed
    /// </summary>
    Task<GrammarPipelineDiagnostics> AnalyzeAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    /// <summary>
    /// Confidence-weighted blended corrections
    /// </summary>
    Task<GrammarBlendedResult> GetBlendedCorrectionsAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    /// <summary>
    /// Train custom rules from user feedback
    /// </summary>
    Task<bool> TrainFromFeedbackAsync(GrammarFeedback feedback, CancellationToken ct = default);
}

public sealed record GrammarPipelineDiagnostics(
    IReadOnlyDictionary<string, EngineContribution> EngineContributions,
    TimeSpan TotalProcessingTime,
    int TotalIssuesFound,
    Dictionary<string, int> IssuesByCategory
);

public sealed record EngineContribution(
    string EngineName,
    int IssuesFound,
    TimeSpan ProcessingTime,
    bool WasPrimary,
    double ConfidenceWeight
);

public sealed record GrammarBlendedResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<BlendedCorrection> Corrections,
    BlendingStrategy UsedStrategy
);

public sealed record BlendedCorrection(
    IReadOnlyList<EngineSuggestion> SourceSuggestions,
    string SelectedSuggestion,
    double BlendedConfidence,
    string SelectionReason
);

public enum BlendingStrategy
{
    ConfidenceWeighted,
    MajorityVote,
    EnginePriority,
    ContextAware
}