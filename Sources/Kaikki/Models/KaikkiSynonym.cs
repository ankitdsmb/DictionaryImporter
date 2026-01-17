namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiSynonym
    {
        [JsonProperty("word")]
        public string Word { get; set; } = null!;

        [JsonProperty("sense")]
        public string? Sense { get; set; }

        [JsonProperty("alt")]
        public string? Alternative { get; set; }
    }
}