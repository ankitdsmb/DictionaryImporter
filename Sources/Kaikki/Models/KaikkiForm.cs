namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiForm
{
    [JsonProperty("form")]
    public string Form { get; set; } = null!;

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = [];
}