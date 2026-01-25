using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Sources.Collins.parsing;

public sealed class CollinsDefinitionParser(ILogger<CollinsDefinitionParser> logger = null)
    : ISourceDictionaryDefinitionParser
{
    public string SourceCode => "ENG_COLLINS";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Definition))
        {
            yield return CreateFallbackParsedDefinition(entry);
            yield break;
        }

        var results = new List<ParsedDefinition>();

        try
        {
            results.Add(ParsingHelperCollins.BuildParsedDefinition(entry));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to parse Collins entry: {Word}", entry.Word);
            results.Clear();
            results.Add(CreateFallbackParsedDefinition(entry));
        }

        foreach (var item in results)
            yield return item;
    }

    private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
    {
        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = entry.Definition ?? string.Empty,
            RawFragment = entry.Definition ?? string.Empty,
            SenseNumber = entry.SenseNumber,
            Domain = null,
            UsageLabel = null,
            CrossReferences = new List<CrossReference>(),
            Synonyms = null,
            Alias = null
        };
    }
}