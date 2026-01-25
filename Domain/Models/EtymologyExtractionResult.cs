namespace DictionaryImporter.Domain.Models;

public sealed class EtymologyExtractionResult
{
    public string? EtymologyText { get; init; }
    public string? LanguageCode { get; init; }
    public string? CleanedDefinition { get; init; }
    public string DetectionMethod { get; init; } = null!;
    public string SourceText { get; init; } = null!;
}