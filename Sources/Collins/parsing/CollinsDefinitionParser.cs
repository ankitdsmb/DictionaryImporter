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

        // Use a separate method to handle the try-catch and return the result
        ParsedDefinition parsedDefinition;
        try
        {
            // Use the helper to parse the Collins entry
            parsedDefinition = ParsingHelperCollins.BuildParsedDefinition(entry);

            // Ensure we have proper sense number
            if (parsedDefinition.SenseNumber <= 0)
            {
                parsedDefinition.SenseNumber = entry.SenseNumber;
            }

            // Ensure we have proper POS
            if (string.IsNullOrEmpty(parsedDefinition.PartOfSpeech) || parsedDefinition.PartOfSpeech == "unk")
            {
                parsedDefinition.PartOfSpeech = entry.PartOfSpeech;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error parsing Collins definition for {Word}", entry.Word);
            parsedDefinition = CreateFallbackParsedDefinition(entry);
        }

        yield return parsedDefinition;
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
            Alias = null,
            PartOfSpeech = entry.PartOfSpeech
        };
    }
}