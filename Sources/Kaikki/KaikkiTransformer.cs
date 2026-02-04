using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.FragmentStore;
using System.Text.Json;
using JsonException = Newtonsoft.Json.JsonException;

namespace DictionaryImporter.Sources.Kaikki;

public sealed class KaikkiTransformer(ILogger<KaikkiTransformer> logger) : IDataTransformer<KaikkiRawEntry>
{
    private const string SourceCode = "KAIKKI";

    public IEnumerable<DictionaryEntry> Transform(KaikkiRawEntry? raw)
    {
        if (raw == null || string.IsNullOrWhiteSpace(raw.RawJson)) yield break;
        if (!Helper.ShouldContinueProcessing(SourceCode, logger)) yield break;

        List<DictionaryEntry> entries;
        try
        {
            entries = TransformInternal(raw).ToList();
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Kaikki JSON");
            yield break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error transforming Kaikki entry");
            yield break;
        }

        foreach (var entry in entries)
            yield return entry;
    }

    private IEnumerable<DictionaryEntry> TransformInternal(KaikkiRawEntry raw)
    {
        using var doc = JsonDocument.Parse(raw.RawJson ?? string.Empty);
        var root = doc.RootElement;

        if (!ParsingHelperKaikki.IsEnglishEntry(root)) yield break;

        var word = Helper.ExtractJsonString(root, "word");
        if (string.IsNullOrWhiteSpace(word)) yield break;

        var normalizedWord = Helper.NormalizeWord(word);
        if (string.IsNullOrWhiteSpace(normalizedWord))
        {
            logger.LogWarning("Failed to normalize word: {Word}", word);
            yield break;
        }

        var definitions = ParsingHelperKaikki.ExtractEnglishDefinitions(root);

        if (definitions.Count == 0)
        {
            definitions = TryExtractShortDefinitions(root);
            if (definitions.Count == 0)
            {
                logger.LogDebug("No definitions found for word: {Word}", word);
                yield break;
            }
        }

        var posRaw = ParsingHelperKaikki.ExtractPartOfSpeechFromJson(root) ?? "unk";
        var pos = Helper.NormalizePartOfSpeech(posRaw);
        var senseNumber = 1;

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition)) continue;

            var normalizedDefinition = Helper.NormalizeDefinition(definition);

            yield return new DictionaryEntry
            {
                Word = word,
                NormalizedWord = normalizedWord,
                PartOfSpeech = pos,
                Definition = normalizedDefinition,
                SenseNumber = senseNumber++,
                SourceCode = SourceCode,
                RawFragment = RawFragments.Save(SourceCode, raw.RawJson, Encoding.UTF8, word),
                CreatedUtc = DateTime.UtcNow
            };
        }

        Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));
    }

    private List<string> TryExtractShortDefinitions(JsonElement root)
    {
        var definitions = new List<string>();

        // Minimal safe fallback extraction (NO fake content generation)
        var alternativeFields = new[] { "senses", "glosses", "raw_glosses", "form_of" };

        foreach (var field in alternativeFields)
        {
            var fieldValue = Helper.ExtractJsonString(root, field);
            if (!string.IsNullOrWhiteSpace(fieldValue))
            {
                definitions.Add(fieldValue);
                continue;
            }

            var array = Helper.ExtractJsonArray(root, field);
            if (array.HasValue)
            {
                foreach (var item in array.Value)
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) definitions.Add(value);
                }
            }
        }

        return definitions;
    }
}