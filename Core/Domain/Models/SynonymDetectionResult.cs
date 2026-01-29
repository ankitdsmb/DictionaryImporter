namespace DictionaryImporter.Core.Domain.Models;

public sealed class SynonymDetectionResult
{
    public string TargetHeadword { get; init; } = null!;
    public string ConfidenceLevel { get; init; } = "high";
    public string DetectionMethod { get; init; } = null!;
    public string SourceText { get; init; } = null!;
}