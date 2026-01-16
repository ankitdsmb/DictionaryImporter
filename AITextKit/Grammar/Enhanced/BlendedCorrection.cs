namespace DictionaryImporter.AITextKit.Grammar.Enhanced;

public sealed record BlendedCorrection(
    IReadOnlyList<EngineSuggestion> SourceSuggestions,
    string SelectedSuggestion,
    double BlendedConfidence,
    string SelectionReason
);