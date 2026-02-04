using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Core.Domain.Models;
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

        // Use the helper to parse the Collins entry
        ParsedDefinition parsedDefinition;
        try
        {
            parsedDefinition = ParsingHelperCollins.BuildParsedDefinition(entry);

            // Ensure we have proper sense number
            if (parsedDefinition.SenseNumber <= 0)
            {
                parsedDefinition.SenseNumber = entry.SenseNumber;
            }

            // Ensure we have proper POS - use CollinsExtractor.NormalizePos
            if (string.IsNullOrEmpty(parsedDefinition.PartOfSpeech) || parsedDefinition.PartOfSpeech == "unk")
            {
                if (!string.IsNullOrEmpty(entry.PartOfSpeech))
                {
                    parsedDefinition.PartOfSpeech = CollinsExtractor.NormalizePos(entry.PartOfSpeech);
                }
                else
                {
                    parsedDefinition.PartOfSpeech = "unk";
                }
            }

            // Ensure examples are clean
            if (parsedDefinition.Examples?.Any() == true)
            {
                parsedDefinition.Examples = parsedDefinition.Examples
                    .Select(e => CleanExample(e))
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .ToList();
            }

            // Log if we found examples
            if (parsedDefinition.Examples?.Any() == true)
            {
                logger?.LogDebug("Found {Count} examples for {Word} sense {Sense}",
                    parsedDefinition.Examples.Count, entry.Word, parsedDefinition.SenseNumber);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error parsing Collins definition for {Word}", entry.Word);
            parsedDefinition = CreateFallbackParsedDefinition(entry);
        }

        yield return parsedDefinition;
    }

    private string CleanExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return example;

        // Remove Chinese characters
        var cleaned = CollinsExtractor.RemoveChineseCharacters(example);

        // Clean up
        cleaned = cleaned.Replace("  ", " ")
                        .Replace(".,.", ".")
                        .Replace("...", ".")
                        .Replace("·", "")
                        .Trim();

        // Ensure proper ending
        if (!string.IsNullOrEmpty(cleaned) &&
            !cleaned.EndsWith(".") &&
            !cleaned.EndsWith("!") &&
            !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
    {
        // Try to extract examples even in fallback
        var examples = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Definition))
        {
            examples = ParsingHelperCollins.ExtractCollinsExamples(entry.Definition)?.ToList() ?? new List<string>();
        }

        // Clean examples
        examples = examples.Select(e => CleanExample(e))
                          .Where(e => !string.IsNullOrWhiteSpace(e))
                          .ToList();

        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = entry.Definition ?? string.Empty,
            RawFragment = entry.RawFragmentLine ?? string.Empty,
            SenseNumber = entry.SenseNumber,
            Domain = null,
            UsageLabel = null,
            CrossReferences = new List<CrossReference>(),
            Synonyms = new List<string>(),
            Alias = null,
            Examples = examples,
            PartOfSpeech = !string.IsNullOrEmpty(entry.PartOfSpeech)
                ? CollinsExtractor.NormalizePos(entry.PartOfSpeech)
                : "unk",
            IPA = null,
            GrammarInfo = null,
            UsageNote = null
        };
    }
}