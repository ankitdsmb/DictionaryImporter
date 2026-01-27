using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Kaikki.Parsing;

public sealed class KaikkiExampleExtractor(ILogger<KaikkiExampleExtractor> logger) : IExampleExtractor
{
    public string SourceCode => "KAIKKI";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.RawFragment))
            return Array.Empty<string>();

        try
        {
            if (!ParsingHelperKaikki.TryParseEnglishRoot(parsed.RawFragment, out _))
                return Array.Empty<string>();

            if (!ParsingHelperKaikki.TryParseJsonRoot(parsed.RawFragment, out var root))
                return Array.Empty<string>();

            if (!root.TryGetProperty("senses", out var senses) || senses.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

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
                return Array.Empty<string>();

            var rawExamples = ExtractExamplesFromSense(senseElement.Value);

            return rawExamples
                .Select(e =>
                    ParsingHelperKaikki.CleanExampleText(
                        ParsingHelperKaikki.CleanKaikkiText(e)))
                .Select(e => e.NormalizeExample())
                .Where(e => e.IsValidExampleSentence())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Kaikki JSON for example extraction");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to extract examples from Kaikki JSON");
        }

        return Array.Empty<string>();
    }

    private static List<string> ExtractExamplesFromSense(JsonElement sense)
    {
        var results = new List<string>();

        if (sense.TryGetProperty("examples", out var examples) &&
            examples.ValueKind == JsonValueKind.Array)
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

                if (ex.ValueKind == JsonValueKind.Object &&
                    ex.TryGetProperty("text", out var textProp) &&
                    textProp.ValueKind == JsonValueKind.String)
                {
                    var s = textProp.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        results.Add(s);
                }
            }
        }

        return results;
    }
}