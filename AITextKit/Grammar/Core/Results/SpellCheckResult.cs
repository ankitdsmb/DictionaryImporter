namespace DictionaryImporter.AITextKit.Grammar.Core.Results;

public record SpellCheckResult(bool IsCorrect, IReadOnlyList<string> Suggestions);