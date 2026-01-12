namespace DictionaryImporter.Core.Parsing;

public sealed class SynonymDetectionResult
{
    public string TargetHeadword { get; init; } = null!;
    public string ConfidenceLevel { get; init; } = "high"; // high | medium | low
    public string DetectionMethod { get; init; } = null!;
    public string SourceText { get; init; } = null!; // Original text that triggered detection
}