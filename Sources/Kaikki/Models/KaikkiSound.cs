namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiSound
    {
        public string? Ipa { get; set; }
        public string? Audio { get; set; }
        public string? AudioUrl { get; set; }
        public string? Tags { get; set; }
        public string? Rhymes { get; set; }
        public string? Enpr { get; set; } // English pronunciation
    }
}