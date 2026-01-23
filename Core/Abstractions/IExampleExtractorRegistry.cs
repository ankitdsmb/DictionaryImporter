namespace DictionaryImporter.Core.Abstractions
{
    public interface IExampleExtractorRegistry
    {
        IExampleExtractor GetExtractor(string sourceCode);

        void Register(IExampleExtractor extractor);
    }
}