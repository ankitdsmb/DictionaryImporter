namespace DictionaryImporter.Core.Parsing
{
    public interface IExampleExtractor
    {
        string SourceCode { get; }

        IReadOnlyList<string> Extract(ParsedDefinition parsed);
    }
}