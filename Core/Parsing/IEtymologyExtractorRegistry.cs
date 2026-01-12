namespace DictionaryImporter.Core.Parsing;

public interface IEtymologyExtractorRegistry
{
    IEtymologyExtractor GetExtractor(string sourceCode);

    void Register(IEtymologyExtractor extractor);

    IReadOnlyDictionary<string, IEtymologyExtractor> GetAllExtractors();
}