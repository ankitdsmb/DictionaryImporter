namespace DictionaryImporter.Core.Grammar;

public record AppliedCorrection(
    string OriginalSegment,
    string Replacement,
    string RuleId,
    string RuleDescription,
    int Confidence
);