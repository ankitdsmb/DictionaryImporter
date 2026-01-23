using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Sources.Parsing;

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
                yield return Helper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            raw = raw.TrimStart();

            if (!ParsingHelperKaikki.IsJsonRawFragment(raw))
            {
                _logger.LogWarning(
                    "KaikkiDefinitionParser skipping non-JSON RawFragment. Word={Word}",
                    entry.Word);

                yield return Helper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var parsedDefinitions = new List<ParsedDefinition>();

            // ✅ Parse safely using helper (clone root + safe fail)
            if (!ParsingHelperKaikki.TryParseJsonRoot(raw, out var root))
            {
                _logger.LogDebug(
                    "KaikkiDefinitionParser JSON invalid/truncated. Using fallback. Word={Word} Len={Len}",
                    entry.Word,
                    raw.Length);

                yield return Helper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            try
            {
                if (!ParsingHelperKaikki.IsEnglishEntry(root))
                    yield break;

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    var senseIndex = 1;

                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (!ParsingHelperKaikki.IsEnglishSense(sense))
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
                    parsedDefinitions.Add(Helper.CreateFallbackParsedDefinition(entry));

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
                parsedDefinitions.Add(Helper.CreateFallbackParsedDefinition(entry));
            }

            foreach (var parsed in parsedDefinitions)
                yield return parsed;
        }

        private ParsedDefinition? ExtractParsedDefinition(JsonElement sense, DictionaryEntry entry, int senseNumber)
        {
            var definition = ParsingHelperKaikki.ExtractDefinitionFromSense(sense);
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            definition = ParsingHelperKaikki.NormalizeBrokenHtmlEntities(definition);
            definition = ParsingHelperKaikki.CleanKaikkiText(definition);

            if (!ParsingHelperKaikki.IsAcceptableEnglishText(definition))
                return null;

            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = definition,
                RawFragment = entry.RawFragment,
                SenseNumber = senseNumber,
                Domain = ParsingHelperKaikki.ExtractDomain(sense),
                UsageLabel = ParsingHelperKaikki.ExtractUsageLabel(sense),
                CrossReferences = ParsingHelperKaikki.ExtractCrossReferences(sense),
                Synonyms = ParsingHelperKaikki.ExtractSynonymsList(sense),
                Alias = null
            };
        }
    }
}
