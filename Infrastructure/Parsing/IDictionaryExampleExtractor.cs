namespace DictionaryImporter.Infrastructure.Parsing;

public interface IDictionaryExampleExtractor
{
    IEnumerable<string> Extract(ParsedDefinition parsedDefinition);
}