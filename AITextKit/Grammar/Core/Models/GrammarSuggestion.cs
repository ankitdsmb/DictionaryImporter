namespace DictionaryImporter.AITextKit.Grammar.Core.Models;

public record GrammarSuggestion(
    string TargetText,
    string Suggestion,
    string Explanation,
    string Category
);