using DictionaryImporter.Gateway.Grammar.Core.Models;

namespace DictionaryImporter.Gateway.Grammar.Core.Results;

public record GrammarCorrectionResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<AppliedCorrection> AppliedCorrections,
    IReadOnlyList<GrammarIssue> RemainingIssues
);