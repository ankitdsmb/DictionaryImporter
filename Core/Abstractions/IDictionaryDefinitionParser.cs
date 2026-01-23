namespace DictionaryImporter.Core.Abstractions
{
    public interface IDictionaryDefinitionParser
    {
        IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry);
    }
}