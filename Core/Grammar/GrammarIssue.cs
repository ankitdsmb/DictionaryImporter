namespace DictionaryImporter.Core.Grammar;

public record GrammarIssue(
    int StartOffset,
    int EndOffset,
    string Message,
    string ShortMessage,
    IReadOnlyList<string> Replacements,
    string RuleId,
    string RuleDescription,
    IReadOnlyList<string> Tags,
    string Context,
    int ContextOffset,
    int ConfidenceLevel
);