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
                // 🔒 Prevent foreign headword bleed (AB-, AB, etc.)
                if (ParsingHelperGutenberg.IsForeignHeadwordBlock(block, entry.Word))
                    continue;

                var headword = ParsingHelperGutenberg.ExtractHeadwordFromBlock(block);
                if (string.IsNullOrWhiteSpace(headword))
                    headword = entry.Word;

                if (!ParsingHelperGutenberg.ShouldProcessWord(headword))
                    continue;

                var partOfSpeech = ParsingHelperGutenberg.ExtractPartOfSpeech(block);
                if (string.IsNullOrWhiteSpace(partOfSpeech))
                    partOfSpeech = ParsingHelperGutenberg.ExtractFallbackPartOfSpeech(entry);

                var definitions = ParsingHelperGutenberg.ExtractDefinitions(block);
                if (definitions == null || definitions.Count == 0)
                    continue;

                var synonyms = ParsingHelperGutenberg.ExtractSynonyms(block);
                var examples = ParsingHelperGutenberg.ExtractExamples(block);
                var etymology = ParsingHelperGutenberg.ExtractEtymology(block);
                var pronunciation = ParsingHelperGutenberg.ExtractPronunciation(block);
                var domains = ParsingHelperGutenberg.ExtractDomains(block);

                var subSenseIndex = 0;

                foreach (var rawDef in definitions)
                {
                    var defText = rawDef?.Trim();
                    if (string.IsNullOrWhiteSpace(defText))
                        continue;

                    // ❌ Skip domain-only or junk definitions
                    if (ParsingHelperGutenberg.IsDomainOnlyDefinition(defText))
                        continue;

                    // ✂ Split usage label from definition
                    var (cleanDefinition, usageLabel) =
                        ParsingHelperGutenberg.SplitUsageFromDefinition(defText);

                    if (string.IsNullOrWhiteSpace(cleanDefinition))
                        continue;

                    subSenseIndex++;

                    var finalSenseNum = entry.SenseNumber > 0
                        ? entry.SenseNumber
                        : subSenseIndex;

                    results.Add(new ParsedDefinition
                    {
                        MeaningTitle = string.Empty, // never the headword
                        Definition = cleanDefinition,
                        RawFragment = block,
                        SenseNumber = finalSenseNum,
                        PartOfSpeech = partOfSpeech,
                        Etymology = etymology,
                        Pronunciation = pronunciation,
                        Domain = domains?.FirstOrDefault(),
                        UsageLabel = usageLabel,
                        CrossReferences = new List<CrossReference>(),
                        Synonyms = synonyms ?? new List<string>(),
                        Alias = null,
                        Examples = examples ?? new List<string>(),
                        DedupKey = ParsingHelperGutenberg.GenerateDedupKey(
                            headword,
                            partOfSpeech,
                            SourceCode)
                    });
                }
            }

            if (results.Count == 0)
            {
                logger?.LogWarning(
                    "No usable definitions extracted from Gutenberg entry: {Word}",
                    entry.Word);
                return Array.Empty<ParsedDefinition>();
            }

            return results;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Failed to parse Gutenberg entry: {Word}",
                entry.Word);

            return Array.Empty<ParsedDefinition>();
        }
    }
}