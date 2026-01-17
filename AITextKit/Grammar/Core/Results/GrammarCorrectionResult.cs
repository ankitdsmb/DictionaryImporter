using DictionaryImporter.AITextKit.Grammar.Core.Models;

namespace DictionaryImporter.AITextKit.Grammar.Core.Results;

public record GrammarCorrectionResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<AppliedCorrection> AppliedCorrections,
    IReadOnlyList<GrammarIssue> RemainingIssues
);