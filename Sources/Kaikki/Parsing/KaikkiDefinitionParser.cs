using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using System.Text.Json;

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
                yield break;

            raw = raw.TrimStart();

            // ✅ FIX: Kaikki parser must only parse JSON fragments
            // Prevent: 'A' is an invalid start of a value
            // Prevent: ',' is invalid after a single JSON value
            // Prevent: '-' is an invalid end of a number
            if (!(raw.StartsWith("{") || raw.StartsWith("[")))
            {
                _logger.LogWarning(
                    "KaikkiDefinitionParser skipping non-JSON RawFragment. Word={Word}",
                    entry.Word);

                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var parsedDefinitions = new List<ParsedDefinition>();

            try
            {
                // ✅ FIX: parse the trimmed raw
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // FIX: Use Kaikki JsonProcessor helper (consistent with transformer)
                if (!JsonProcessor.IsEnglishEntry(root))
                {
                    _logger.LogDebug("Skipping non-English entry: {Word}", entry.Word);
                    yield break;
                }

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    var senseIndex = 1;

                    foreach (var sense in senses.EnumerateArray())
                    {
                        // FIX: Use Kaikki JsonProcessor helper (safe)
                        if (!JsonProcessor.IsEnglishSense(sense))
                            continue;

                        var parsed = ExtractParsedDefinition(sense, entry, senseIndex);

                        // FIX: Never add null
                        if (parsed != null)
                        {
                            parsedDefinitions.Add(parsed);
                            senseIndex++;
                        }
                    }
                }

                if (parsedDefinitions.Count == 0)
                {
                    parsedDefinitions.Add(SourceDataHelper.CreateFallbackParsedDefinition(entry));
                }
            }
            catch (System.Text.Json.JsonException ex) // ✅ FIX: correct exception type
            {
                _logger.LogError(ex, "Failed to parse JSON for entry: {Word}", entry.Word);
                parsedDefinitions.Add(SourceDataHelper.CreateFallbackParsedDefinition(entry));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error parsing entry: {Word}", entry.Word);
                parsedDefinitions.Add(SourceDataHelper.CreateFallbackParsedDefinition(entry));
            }

            foreach (var parsed in parsedDefinitions)
                yield return parsed;
        }

        private ParsedDefinition? ExtractParsedDefinition(JsonElement sense, DictionaryEntry entry, int senseNumber)
        {
            var definition = ExtractDefinitionFromSense(sense);
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = definition,
                RawFragment = entry.RawFragment,
                SenseNumber = senseNumber,
                Domain = SourceDataHelper.ExtractDomain(sense),
                UsageLabel = SourceDataHelper.ExtractUsageLabel(sense),
                CrossReferences = SourceDataHelper.ExtractCrossReferences(sense),
                Synonyms = ExtractSynonymsList(sense),
                Alias = null
            };
        }

        private static string? ExtractDefinitionFromSense(JsonElement sense)
        {
            if (sense.TryGetProperty("glosses", out var glosses) && glosses.ValueKind == JsonValueKind.Array)
            {
                foreach (var gloss in glosses.EnumerateArray())
                {
                    if (gloss.ValueKind != JsonValueKind.String)
                        continue;

                    var definition = gloss.GetString()?.Trim();

                    if (!string.IsNullOrWhiteSpace(definition) &&
                        definition.Length > 3 &&
                        !definition.Contains("→") &&
                        !definition.StartsWith("{") &&
                        !definition.Contains("\"lang\":"))
                    {
                        return definition;
                    }
                }
            }

            if (sense.TryGetProperty("raw_glosses", out var rawGlosses) && rawGlosses.ValueKind == JsonValueKind.Array)
            {
                foreach (var rawGloss in rawGlosses.EnumerateArray())
                {
                    if (rawGloss.ValueKind != JsonValueKind.String)
                        continue;

                    var definition = rawGloss.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(definition))
                        return definition;
                }
            }

            return null;
        }

        private static List<string> ExtractSynonymsList(JsonElement sense)
        {
            var synonyms = new List<string>();

            if (sense.TryGetProperty("synonyms", out var synonymsArray) && synonymsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var synonym in synonymsArray.EnumerateArray())
                {
                    if (synonym.ValueKind != JsonValueKind.Object)
                        continue;

                    if (synonym.TryGetProperty("word", out var word) && word.ValueKind == JsonValueKind.String)
                    {
                        var synonymWord = word.GetString();
                        if (!string.IsNullOrWhiteSpace(synonymWord))
                            synonyms.Add(synonymWord);
                    }
                }
            }

            return synonyms;
        }
    }
}