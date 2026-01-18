namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiExample
    {
        public string Text { get; set; } = null!;
        public Dictionary<string, object>? RawData { get; set; }
        public string? Type { get; set; }
    }
}