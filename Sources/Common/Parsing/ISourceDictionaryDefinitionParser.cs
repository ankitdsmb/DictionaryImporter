namespace DictionaryImporter.Sources.Common.Parsing
{
    public interface ISourceDictionaryDefinitionParser : IDictionaryDefinitionParser
    {
        string SourceCode { get; }
    }
}