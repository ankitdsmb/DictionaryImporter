namespace DictionaryImporter.Sources.Common.Parsing
{
    public interface IDictionaryDefinitionParserResolver
    {
        IDictionaryDefinitionParser Resolve(string sourceCode);
    }
}