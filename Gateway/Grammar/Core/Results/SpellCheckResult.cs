namespace DictionaryImporter.Gateway.Grammar.Core.Results
{
    public record SpellCheckResult(bool IsCorrect, IReadOnlyList<string> Suggestions);
}