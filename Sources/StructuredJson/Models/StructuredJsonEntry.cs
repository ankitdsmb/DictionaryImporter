using System.Text.Json.Serialization;

namespace DictionaryImporter.Sources.StructuredJson.Models;

internal sealed class StructuredJsonEntry
{
    [JsonPropertyName("original_cased_word")]
    public string OriginalCasedWord { get; set; } = null!;

    [JsonPropertyName("transliterated_word")]
    public string TransliteratedWord { get; set; } = null!;

    [JsonPropertyName("definitions")] public List<StructuredJsonDefinition> Definitions { get; set; } = new();
}