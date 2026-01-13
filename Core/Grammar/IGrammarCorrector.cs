// File: DictionaryImporter/Core/Grammar/Abstractions.cs
using DictionaryImporter.Core.Grammar.Enhanced;

namespace DictionaryImporter.Core.Grammar;

public interface IGrammarCorrector
{
    Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default);
}

public interface ITrainableGrammarEngine
{
    Task TrainAsync(GrammarFeedback feedback, CancellationToken ct = default);
}

public record GrammarCheckResult(
    bool HasIssues,
    int IssueCount,
    IReadOnlyList<GrammarIssue> Issues,
    TimeSpan ElapsedTime
);

public record GrammarCorrectionResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<AppliedCorrection> AppliedCorrections,
    IReadOnlyList<GrammarIssue> RemainingIssues
);

public record GrammarIssue(
    int StartOffset,
    int EndOffset,
    string Message,
    string ShortMessage,
    IReadOnlyList<string> Replacements,
    string RuleId,
    string RuleDescription,
    IReadOnlyList<string> Tags,
    string Context,
    int ContextOffset,
    int ConfidenceLevel
);

public record AppliedCorrection(
    string OriginalSegment,
    string Replacement,
    string RuleId,
    string RuleDescription,
    int Confidence
);

public record GrammarSuggestion(
    string TargetText,
    string Suggestion,
    string Explanation,
    string Category
);