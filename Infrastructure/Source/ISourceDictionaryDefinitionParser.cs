namespace DictionaryImporter.Infrastructure.Source;

public interface ISourceDictionaryDefinitionParser : IDictionaryDefinitionParser
{
    string SourceCode { get; }
}