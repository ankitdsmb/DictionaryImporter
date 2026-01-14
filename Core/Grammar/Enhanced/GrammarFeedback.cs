namespace DictionaryImporter.Core.Grammar.Enhanced;

public sealed class GrammarFeedback
{
    public string OriginalText { get; set; } = null!;
    public string? CorrectedText { get; set; }
    public GrammarIssue? OriginalIssue { get; set; }
    public bool IsValidCorrection { get; set; }
    public bool IsFalsePositive { get; set; }
    public string? UserComment { get; set; }

    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset Timestamp { get; set; }
}