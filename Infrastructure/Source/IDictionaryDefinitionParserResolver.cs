namespace DictionaryImporter.Infrastructure.Source;

public interface IDictionaryDefinitionParserResolver
{
    IDictionaryDefinitionParser Resolve(string sourceCode);
}