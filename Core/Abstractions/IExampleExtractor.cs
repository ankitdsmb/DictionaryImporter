namespace DictionaryImporter.Core.Abstractions;

public interface IExampleExtractor
{
    string SourceCode { get; }

    IReadOnlyList<string> Extract(ParsedDefinition parsed);
}