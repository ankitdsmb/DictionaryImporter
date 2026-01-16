namespace DictionaryImporter.AITextKit.Grammar;

public record GrammarCheckResult(
    bool HasIssues,
    int IssueCount,
    IReadOnlyList<GrammarIssue> Issues,
    TimeSpan ElapsedTime
);