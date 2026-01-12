// Add in DictionaryImporter.Core.Parsing namespace
using DictionaryImporter.Domain.Models;

namespace DictionaryImporter.Core.Parsing
{
    public interface IExampleExtractor
    {
        string SourceCode { get; }
        IReadOnlyList<string> Extract(ParsedDefinition parsed);
    }

    public interface IExampleExtractorRegistry
    {
        IExampleExtractor GetExtractor(string sourceCode);
        void Register(IExampleExtractor extractor);
    }
}