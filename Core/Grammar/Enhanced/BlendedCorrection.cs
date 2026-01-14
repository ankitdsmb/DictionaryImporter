namespace DictionaryImporter.Core.Grammar.Enhanced;

public sealed record BlendedCorrection(
    IReadOnlyList<EngineSuggestion> SourceSuggestions,
    string SelectedSuggestion,
    double BlendedConfidence,
    string SelectionReason
);