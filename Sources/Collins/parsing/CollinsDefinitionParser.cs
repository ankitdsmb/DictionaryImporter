using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Sources.Collins.parsing;

public sealed class CollinsDefinitionParser(ILogger<CollinsDefinitionParser> logger = null)
    : ISourceDictionaryDefinitionParser
{
    public string SourceCode => "ENG_COLLINS";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        if (entry == null)
        {
            logger?.LogWarning("Received null entry");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(entry.Definition))
        {
            logger?.LogDebug("Empty definition for entry: {Word}", entry.Word);
            yield return CreateFallbackParsedDefinition(entry);
            yield break;
        }

        var results = new List<ParsedDefinition>();

        try
        {
            // First clean the definition of Chinese characters
            var cleanedDefinition = CollinsExtractor.RemoveChineseCharacters(entry.Definition);

            // Create a copy with cleaned definition for parsing
            var cleanedEntry = new DictionaryEntry
            {
                Word = entry.Word,
                NormalizedWord = entry.NormalizedWord,
                PartOfSpeech = entry.PartOfSpeech,
                Definition = cleanedDefinition,
                SenseNumber = entry.SenseNumber,
                SourceCode = entry.SourceCode,
                CreatedUtc = entry.CreatedUtc,
                RawFragment = cleanedDefinition
            };

            var parsedDefinition = ParsingHelperCollins.BuildParsedDefinition(cleanedEntry);
            results.Add(parsedDefinition);
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