namespace DictionaryImporter.Core.Grammar;

public interface IGrammarCorrector
{
    Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default);
}

public sealed record GrammarCheckResult(
    bool HasIssues,
    int IssueCount,
    IReadOnlyList<GrammarIssue> Issues,
    TimeSpan ProcessingTime
);

public sealed record GrammarIssue(
    string RuleId,
    string Message,
    string Category,
    int StartOffset,
    int EndOffset,
    IReadOnlyList<string> Replacements,
    int ConfidenceLevel // 0-100
);

public sealed record GrammarCorrectionResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<AppliedCorrection> AppliedCorrections,
    IReadOnlyList<GrammarIssue> RemainingIssues
);

public sealed record AppliedCorrection(
    string OriginalSegment,
    string CorrectedSegment,
    string RuleId,
    string RuleDescription,
    int Confidence
);

public sealed record GrammarSuggestion(
    string OriginalSegment,
    string SuggestedImprovement,
    string Reason,
    string Impact // "clarity", "conciseness", "formality", etc.
);