namespace DictionaryImporter.Sources.Parsing
{
    public interface ISourceDictionaryDefinitionParser : IDictionaryDefinitionParser
    {
        string SourceCode { get; }
    }
}