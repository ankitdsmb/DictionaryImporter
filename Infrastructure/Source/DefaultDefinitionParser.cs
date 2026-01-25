namespace DictionaryImporter.Infrastructure.Source;

public sealed class DefaultDefinitionParser : IDictionaryDefinitionParser
{
    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        yield return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = entry.Definition,
            RawFragment = entry.RawFragment,
            SenseNumber = entry.SenseNumber,
            Domain = null,
            UsageLabel = null,
            CrossReferences = new List<CrossReference>(),
            Synonyms = null,
            Alias = null
        };
    }
}