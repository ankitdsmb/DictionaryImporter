using Newtonsoft.Json;

namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiExample
{
    [JsonProperty("text")]
    public string Text { get; set; } = null!;

    [JsonProperty("type")]
    public string? Type { get; set; }
}