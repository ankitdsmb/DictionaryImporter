namespace DictionaryImporter.Core.Abstractions;

public interface ISynonymExtractorRegistry
{
    ISynonymExtractor GetExtractor(string sourceCode);
}