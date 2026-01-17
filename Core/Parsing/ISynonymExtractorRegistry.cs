namespace DictionaryImporter.Core.Parsing
{
    public interface ISynonymExtractorRegistry
    {
        ISynonymExtractor GetExtractor(string sourceCode);
    }
}