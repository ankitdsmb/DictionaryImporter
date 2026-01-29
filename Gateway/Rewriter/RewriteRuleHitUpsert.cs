namespace DictionaryImporter.Gateway.Rewriter;

public sealed class RewriteRuleHitUpsert
{
    public string SourceCode { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string RuleType { get; init; } = string.Empty;
    public string RuleKey { get; init; } = string.Empty;
    public long HitCount { get; init; } = 1;
}