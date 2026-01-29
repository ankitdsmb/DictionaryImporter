namespace DictionaryImporter.Gateway.Rewriter;

public sealed class TitleCaseResult(
    string text,
    bool changed,
    string? reason,
    Dictionary<string, object>? metrics = null)
{
    public string Text { get; } = text;
    public bool Changed { get; } = changed;
    public string? Reason { get; } = reason;
    public Dictionary<string, object>? Metrics { get; } = metrics;
}