namespace DictionaryImporter.AITextKit.Grammar;

public record GrammarSuggestion(
    string TargetText,
    string Suggestion,
    string Explanation,
    string Category
);