namespace DictionaryImporter.Core.Grammar.Enhanced;

#region Engine Models

public sealed record EngineSuggestion(
    string EngineName,
    string SuggestedText,
    double Confidence,
    string RuleId,
    string Explanation
);

#endregion Engine Models