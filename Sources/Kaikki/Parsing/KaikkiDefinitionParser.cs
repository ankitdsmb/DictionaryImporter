using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Kaikki.Parsing
{
    public sealed class KaikkiDefinitionParser : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "KAIKKI";

        private readonly ILogger<KaikkiDefinitionParser> _logger;

        public KaikkiDefinitionParser(ILogger<KaikkiDefinitionParser> logger)
        {
            _logger = logger;
        }

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry? entry)
        {
            if (entry is null)
                yield break;

            var raw = entry.RawFragment;

            if (string.IsNullOrWhiteSpace(raw))
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            raw = raw.TrimStart();

            if (!KaikkiParsingHelper.IsJsonRawFragment(raw))
            {
                _logger.LogWarning(
                    "KaikkiDefinitionParser skipping non-JSON RawFragment. Word={Word}",
                    entry.Word);

                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var parsedDefinitions = new List<ParsedDefinition>();

            // ✅ Parse safely using helper (clone root + safe fail)
            if (!KaikkiParsingHelper.TryParseJsonRoot(raw, out var root))
            {
                _logger.LogDebug(
                    "KaikkiDefinitionParser JSON invalid/truncated. Using fallback. Word={Word} Len={Len}",
                    entry.Word,
                    raw.Length);

                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            try
            {
                if (!KaikkiParsingHelper.IsEnglishEntry(root))
                    yield break;

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    var senseIndex = 1;

                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (!KaikkiParsingHelper.IsEnglishSense(sense))
                            continue;

                        var parsed = ExtractParsedDefinition(sense, entry, senseIndex);

                        if (parsed != null)
                        {
                            parsedDefinitions.Add(parsed);
                            senseIndex++;
                        }
                    }
                }

                if (parsedDefinitions.Count == 0)
                    parsedDefinitions.Add(SourceDataHelper.CreateFallbackParsedDefinition(entry));

                parsedDefinitions = parsedDefinitions
                    .GroupBy(d => $"{d.MeaningTitle}|{d.SenseNumber}|{(d.Definition ?? "").Trim()}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }
            catch (Exception ex)
            {
                // ✅ Never crash import
                _logger.LogDebug(
                    ex,
                    "KaikkiDefinitionParser unexpected error (fallback used). Word={Word}",
                    entry.Word);

                parsedDefinitions.Clear();
                parsedDefinitions.Add(SourceDataHelper.CreateFallbackParsedDefinition(entry));
            }

            foreach (var parsed in parsedDefinitions)
                yield return parsed;
        }

        private ParsedDefinition? ExtractParsedDefinition(JsonElement sense, DictionaryEntry entry, int senseNumber)
        {
            var definition = KaikkiParsingHelper.ExtractDefinitionFromSense(sense);
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            definition = KaikkiParsingHelper.NormalizeBrokenHtmlEntities(definition);
            definition = KaikkiParsingHelper.CleanKaikkiText(definition);

            if (!KaikkiParsingHelper.IsAcceptableEnglishText(definition))
                return null;

            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = definition,
                RawFragment = entry.RawFragment,
                SenseNumber = senseNumber,
                Domain = KaikkiParsingHelper.ExtractDomain(sense),
                UsageLabel = KaikkiParsingHelper.ExtractUsageLabel(sense),
                CrossReferences = KaikkiParsingHelper.ExtractCrossReferences(sense),
                Synonyms = KaikkiParsingHelper.ExtractSynonymsList(sense),
                Alias = null
            };
        }
    }
}
