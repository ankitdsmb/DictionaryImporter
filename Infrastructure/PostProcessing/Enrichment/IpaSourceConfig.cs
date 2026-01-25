namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment;

public sealed class IpaSourceConfig
{
    public string Locale { get; init; } = default!;
    public string FilePath { get; init; } = default!;
}