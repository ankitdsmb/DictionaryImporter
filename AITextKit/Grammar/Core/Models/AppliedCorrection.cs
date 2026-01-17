namespace DictionaryImporter.AITextKit.Grammar.Core.Models;

public record AppliedCorrection(
    string OriginalSegment,
    string Replacement,
    string RuleId,
    string RuleDescription,
    int Confidence
);