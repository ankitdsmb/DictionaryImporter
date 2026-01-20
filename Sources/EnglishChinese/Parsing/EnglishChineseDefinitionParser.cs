using System;
using System.Collections.Generic;
using System.Linq;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.EnglishChinese.Parsing
{
    public sealed class EnglishChineseDefinitionParser : ISourceDictionaryDefinitionParser
    {
        private readonly ILogger<EnglishChineseDefinitionParser> _logger;

        public EnglishChineseDefinitionParser(ILogger<EnglishChineseDefinitionParser> logger = null)
        {
            _logger = logger;
        }

        public string SourceCode => "ENG_CHN";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            if (entry == null)
            {
                yield return CreateFallbackParsedDefinition(entry);
                yield break;
            }

            // Use RawFragment if available
            var rawDefinition = !string.IsNullOrWhiteSpace(entry.RawFragment)
                ? entry.RawFragment
                : entry.Definition;

            if (string.IsNullOrWhiteSpace(rawDefinition))
            {
                yield return CreateFallbackParsedDefinition(entry);
                yield break;
            }

            string definition;
            string partOfSpeech;

            try
            {
                // ✅ Use SimpleEngChnExtractor for consistent extraction
                definition = SimpleEngChnExtractor.ExtractDefinition(rawDefinition);

                // ✅ Extract part of speech
                partOfSpeech = SimpleEngChnExtractor.ExtractPartOfSpeech(rawDefinition);

                // Log if extraction seems problematic
                if (string.IsNullOrWhiteSpace(definition))
                {
                    _logger?.LogWarning(
                        "Empty definition extracted for {Word}. Raw: {RawPreview}",
                        entry.Word,
                        GetPreview(rawDefinition, 50));
                    definition = rawDefinition; // Fallback to raw
                }

                // Log successful extraction
                _logger?.LogDebug(
                    "ENG_CHN parsed | Word={Word} | POS={POS} | DefinitionPreview={Preview} | HasChinese={HasChinese}",
                    entry.Word,
                    partOfSpeech,
                    GetPreview(definition, 30),
                    SimpleEngChnExtractor.ContainsChinese(definition));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to extract definition for {Word}", entry.Word);
                definition = rawDefinition; // Fallback to raw
                partOfSpeech = null;
            }

            yield return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = definition,
                RawFragment = rawDefinition,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = partOfSpeech, // ✅ Store POS in UsageLabel
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry?.Word ?? "unnamed sense",
                Definition = entry?.Definition ?? string.Empty,
                RawFragment = entry?.RawFragment ?? entry?.Definition ?? string.Empty,
                SenseNumber = entry?.SenseNumber ?? 1,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        private string GetPreview(string text, int length)
        {
            if (string.IsNullOrWhiteSpace(text)) return "[empty]";
            if (text.Length <= length) return text;
            return text.Substring(0, length) + "...";
        }
    }
}