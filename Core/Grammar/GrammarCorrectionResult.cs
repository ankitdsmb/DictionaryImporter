namespace DictionaryImporter.Core.Grammar;

public record GrammarCorrectionResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<AppliedCorrection> AppliedCorrections,
    IReadOnlyList<GrammarIssue> RemainingIssues
);