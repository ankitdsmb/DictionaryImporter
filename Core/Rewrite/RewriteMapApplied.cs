namespace DictionaryImporter.Core.Rewrite;

public sealed record RewriteMapApplied(
    long RuleId,
    string FromText,
    string ToText,
    int Priority,
    bool IsRegex,
    bool WholeWord);