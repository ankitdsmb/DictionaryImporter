// File: Sources/Kaikki/Parsing/KaikkiExampleExtractor.cs

using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Kaikki.Parsing
{
    public sealed class KaikkiExampleExtractor(ILogger<KaikkiExampleExtractor> logger) : IExampleExtractor
    {
        public string SourceCode => "KAIKKI";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            try
            {
                if (parsed == null)
                    return examples;

                if (string.IsNullOrWhiteSpace(parsed.RawFragment))
                    return examples;

                // Only parse English root entries
                if (!ParsingHelperKaikki.TryParseEnglishRoot(parsed.RawFragment, out _))
                    return examples;

                // FIX:
                // Previously we extracted examples from the entire raw fragment (whole entry JSON),
                // which repeats the same examples for every sense.
                // Now we extract examples from the specific sense corresponding to parsed.SenseNumber.
                if (!ParsingHelperKaikki.TryParseJsonRoot(parsed.RawFragment, out var root))
                    return examples;

                if (!root.TryGetProperty("senses", out var senses) || senses.ValueKind != JsonValueKind.Array)
                    return examples;

                var senseIndex = parsed.SenseNumber <= 0 ? 1 : parsed.SenseNumber;
                var zeroBasedIndex = senseIndex - 1;

                JsonElement? senseElement = null;

                var idx = 0;
                foreach (var sense in senses.EnumerateArray())
                {
                    if (idx == zeroBasedIndex)
                    {
                        senseElement = sense;
                        break;
                    }

                    idx++;
                }

                if (senseElement == null)
                    return examples;

                // Extract examples ONLY from this sense
                examples = ExtractExamplesFromSense(senseElement.Value);

                for (var i = 0; i < examples.Count; i++)
                {
                    var cleaned =
                        ParsingHelperKaikki.CleanExampleText(
                            ParsingHelperKaikki.CleanKaikkiText(examples[i]));

                    examples[i] = cleaned;
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                logger.LogDebug(ex, "Failed to parse Kaikki JSON for example extraction");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract examples from Kaikki JSON");
            }

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ExtractExamplesFromSense(JsonElement sense)
        {
            var results = new List<string>();

            // Kaikki/Wiktionary-json usually stores examples as objects with "text"
            // Example:
            // "examples": [{ "text": "..." }, ...]
            if (sense.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array)
            {
                foreach (var ex in examples.EnumerateArray())
                {
                    if (ex.ValueKind == JsonValueKind.String)
                    {
                        var s = ex.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            results.Add(s);
                        continue;
                    }

                    if (ex.ValueKind == JsonValueKind.Object)
                    {
                        if (ex.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            var s = textProp.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                results.Add(s);
                        }
                    }
                }
            }

            return results;
        }
    }
}
