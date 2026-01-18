namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiSynonym
    {
        public string Word { get; set; } = null!;

        public string? Sense { get; set; }

        public string? Alternative { get; set; }
        public string? Language { get; internal set; }
    }
}