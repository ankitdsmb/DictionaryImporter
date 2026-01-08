using DictionaryImporter.Domain.Models;

namespace DictionaryImporter.Core.Parsing
{
    public interface IDictionaryDefinitionParser
    {
        IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry);
    }
}
