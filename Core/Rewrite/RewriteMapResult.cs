namespace DictionaryImporter.Core.Rewrite;

public sealed record RewriteMapResult(
    string OriginalText,
    string RewrittenText,
    IReadOnlyList<RewriteMapApplied> Applied,
    int AppliedCount);