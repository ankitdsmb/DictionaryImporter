namespace DictionaryImporter.Core.Grammar;

public record SpellCheckResult(bool IsCorrect, IReadOnlyList<string> Suggestions);