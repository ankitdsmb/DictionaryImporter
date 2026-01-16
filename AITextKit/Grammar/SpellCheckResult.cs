namespace DictionaryImporter.AITextKit.Grammar;

public record SpellCheckResult(bool IsCorrect, IReadOnlyList<string> Suggestions);