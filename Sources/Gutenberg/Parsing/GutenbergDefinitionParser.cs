using System;
using System.Collections.Generic;
using System.Linq;
using DictionaryImporter.Infrastructure.Source;
using DictionaryImporter.Sources.Common.Helper;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Gutenberg.Parsing;

public sealed class GutenbergDefinitionParser(ILogger<GutenbergDefinitionParser> logger = null)
    : ISourceDictionaryDefinitionParser
{
    public string SourceCode => "GUT_WEBSTER";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Definition))
            return Array.Empty<ParsedDefinition>();

        try
        {
            var cleanedFullText = ParsingHelperGutenberg.CleanRawFragment(entry.Definition);
            var blocks = ParsingHelperGutenberg.SplitIntoEntryBlocks(cleanedFullText);

            if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(cleanedFullText))
                blocks.Add(cleanedFullText);

            var results = new List<ParsedDefinition>();

            foreach (var block in blocks)
            {
                var headword = ParsingHelperGutenberg.ExtractHeadwordFromBlock(block);

                if (string.IsNullOrWhiteSpace(headword))
                    headword = entry.Word;

                if (!ParsingHelperGutenberg.ShouldProcessWord(headword))
                    continue;

                var partOfSpeech = ParsingHelperGutenberg.ExtractPartOfSpeech(block);
                if (string.IsNullOrWhiteSpace(partOfSpeech))
                    partOfSpeech = ParsingHelperGutenberg.ExtractFallbackPartOfSpeech(entry);

                var definitions = ParsingHelperGutenberg.ExtractDefinitions(block);

                // ✅ IMPORTANT FIX:
                // If no usable definitions extracted -> skip block
                // Do NOT insert fake row "No definition available."
                if (definitions == null || definitions.Count == 0)
                    continue;

                var synonyms = ParsingHelperGutenberg.ExtractSynonyms(block);
                var examples = ParsingHelperGutenberg.ExtractExamples(block);
                var etymology = ParsingHelperGutenberg.ExtractEtymology(block);
                var pronunciation = ParsingHelperGutenberg.ExtractPronunciation(block);
                var domains = ParsingHelperGutenberg.ExtractDomains(block);

                var subSenseIndex = 0;

                foreach (var defText in definitions)
                {
                    subSenseIndex++;

                    var finalSenseNum = subSenseIndex;
                    if (entry.SenseNumber > 0)
                        finalSenseNum = entry.SenseNumber;

                    var parsed = new ParsedDefinition
                    {
                        // ✅ IMPORTANT FIX:
                        // MeaningTitle must not be headword. Keep empty.
                        // Your UI already has headword in DictionaryEntry.Word.
                        MeaningTitle = string.Empty,

                        Definition = defText,
                        RawFragment = block,
                        SenseNumber = finalSenseNum,
                        PartOfSpeech = partOfSpeech,
                        Etymology = etymology,
                        Pronunciation = pronunciation,
                        Domain = domains?.FirstOrDefault(),
                        UsageLabel = null,
                        CrossReferences = new List<CrossReference>(),
                        Synonyms = synonyms ?? new List<string>(),
                        Alias = null,
                        Examples = examples ?? new List<string>(),
                        DedupKey = ParsingHelperGutenberg.GenerateDedupKey(headword, partOfSpeech, SourceCode)
                    };

                    results.Add(parsed);
                }
            }

            if (results.Count == 0)
            {
                logger?.LogWarning("No definitions extracted from Gutenberg entry: {Word}", entry.Word);

                // ✅ IMPORTANT FIX:
                // Return empty, do NOT add fallback nonsense rows.
                return Array.Empty<ParsedDefinition>();
            }

            return results;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to parse Gutenberg entry: {Word}", entry.Word);

            // ✅ IMPORTANT FIX:
            // Return empty, do NOT add fallback nonsense rows.
            return Array.Empty<ParsedDefinition>();
        }
    }
}