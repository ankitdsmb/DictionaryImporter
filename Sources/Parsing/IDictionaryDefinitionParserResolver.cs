namespace DictionaryImporter.Sources.Parsing
{
    public interface IDictionaryDefinitionParserResolver
    {
        IDictionaryDefinitionParser Resolve(string sourceCode);
    }
}