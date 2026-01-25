namespace DictionaryImporter.Core.Abstractions;

public interface IEtymologyExtractorRegistry
{
    IEtymologyExtractor GetExtractor(string sourceCode);

    void Register(IEtymologyExtractor extractor);

    IReadOnlyDictionary<string, IEtymologyExtractor> GetAllExtractors();
}