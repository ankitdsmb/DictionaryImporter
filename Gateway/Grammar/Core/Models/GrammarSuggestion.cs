namespace DictionaryImporter.Gateway.Grammar.Core.Models
{
    public record GrammarSuggestion(
        string TargetText,
        string Suggestion,
        string Explanation,
        string Category
    );
}