using DictionaryImporter.Gateway.Grammar.Core.Models;

namespace DictionaryImporter.Gateway.Grammar.Core.Results
{
    public record GrammarCheckResult(
        bool HasIssues,
        int IssueCount,
        IReadOnlyList<GrammarIssue> Issues,
        TimeSpan ElapsedTime
    );
}