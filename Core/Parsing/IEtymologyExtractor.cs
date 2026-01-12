namespace DictionaryImporter.Core.Parsing;

public interface IEtymologyExtractor
{
    string SourceCode { get; }

    EtymologyExtractionResult Extract(
        string headword,
        string definition,
        string? rawDefinition = null);

    (string? Etymology, string? LanguageCode) ExtractFromText(string text);
}