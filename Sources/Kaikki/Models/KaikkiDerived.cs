namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiDerived
{
    [JsonProperty("word")]
    public string Word { get; set; } = null!;

    [JsonProperty("sense")]
    public string? Sense { get; set; }
}