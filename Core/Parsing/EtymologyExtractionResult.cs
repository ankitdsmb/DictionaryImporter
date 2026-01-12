namespace DictionaryImporter.Core.Parsing;

public sealed class EtymologyExtractionResult
{
    public string? EtymologyText { get; init; }
    public string? LanguageCode { get; init; }
    public string? CleanedDefinition { get; init; } // Definition with etymology removed
    public string DetectionMethod { get; init; } = null!;
    public string SourceText { get; init; } = null!;
}