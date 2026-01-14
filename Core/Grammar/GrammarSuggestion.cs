namespace DictionaryImporter.Core.Grammar;

public record GrammarSuggestion(
    string TargetText,
    string Suggestion,
    string Explanation,
    string Category
);