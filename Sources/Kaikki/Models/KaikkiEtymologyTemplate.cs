using Newtonsoft.Json;

namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiEtymologyTemplate
{
    [JsonProperty("name")]
    public string Name { get; set; } = null!;

    [JsonProperty("args")]
    public Dictionary<string, string> Args { get; set; } = new();
}