// SynonymDetectionResult.cs
namespace DictionaryImporter.Core.Parsing
{
    public interface ISynonymExtractor
    {
        string SourceCode { get; }

        IReadOnlyList<SynonymDetectionResult> Extract(
            string headword,
            string definition,
            string? rawDefinition = null);

        bool ValidateSynonymPair(string headwordA, string headwordB);
    }
    public interface ISynonymExtractorRegistry
    {
        ISynonymExtractor GetExtractor(string sourceCode);
    }
    public sealed class SynonymDetectionResult
    {
        public string TargetHeadword { get; init; } = null!;
        public string ConfidenceLevel { get; init; } = "high"; // high | medium | low
        public string DetectionMethod { get; init; } = null!;
        public string SourceText { get; init; } = null!; // Original text that triggered detection
    }
}