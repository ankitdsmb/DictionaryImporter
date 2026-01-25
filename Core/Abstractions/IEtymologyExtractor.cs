namespace DictionaryImporter.Core.Abstractions;

public interface IEtymologyExtractor
{
    string SourceCode { get; }

    EtymologyExtractionResult Extract(
        string headword,
        string definition,
        string? rawDefinition = null);
}