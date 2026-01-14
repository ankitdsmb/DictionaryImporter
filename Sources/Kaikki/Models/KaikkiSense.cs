using Newtonsoft.Json;

namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiSense
{
    [JsonProperty("glosses")]
    public List<string> Glosses { get; set; } = [];

    [JsonProperty("examples")]
    public List<KaikkiExample>? Examples { get; set; }

    [JsonProperty("categories")]
    public List<string>? Categories { get; set; }

    [JsonProperty("topics")]
    public List<string>? Topics { get; set; }

    [JsonProperty("tags")]
    public List<string>? Tags { get; set; }

    [JsonProperty("senseid")]
    public string? SenseId { get; set; }

    [JsonProperty("raw_glosses")]
    public List<string>? RawGlosses { get; set; }
}