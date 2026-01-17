using DictionaryImporter.AITextKit.Grammar.Core.Models;

namespace DictionaryImporter.AITextKit.Grammar.Core.Results;

public record GrammarCheckResult(
    bool HasIssues,
    int IssueCount,
    IReadOnlyList<GrammarIssue> Issues,
    TimeSpan ElapsedTime
);