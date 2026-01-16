namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiSound
{
    [JsonProperty("ipa")]
    public string? Ipa { get; set; }

    [JsonProperty("tags")]
    public List<string>? Tags { get; set; }

    [JsonProperty("audio")]
    public string? AudioUrl { get; set; }

    [JsonProperty("ogg_url")]
    public string? OggUrl { get; set; }

    [JsonProperty("mp3_url")]
    public string? Mp3Url { get; set; }

    [JsonProperty("text")]
    public string? Text { get; set; }
}