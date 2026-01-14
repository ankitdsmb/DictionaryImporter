namespace DictionaryImporter.Core.Grammar.Enhanced;

public sealed record GrammarBlendedResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<BlendedCorrection> Corrections,
    BlendingStrategy UsedStrategy
);