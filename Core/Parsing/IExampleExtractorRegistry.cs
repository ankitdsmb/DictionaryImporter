namespace DictionaryImporter.Core.Parsing
{
    public interface IExampleExtractorRegistry
    {
        IExampleExtractor GetExtractor(string sourceCode);

        void Register(IExampleExtractor extractor);
    }
}