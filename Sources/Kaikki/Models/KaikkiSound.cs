namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiSound
    {
        public string? Ipa { get; set; }

        public string? Tags { get; set; }

        public string? AudioUrl { get; set; }

        public string? OggUrl { get; set; }

        public string? Mp3Url { get; set; }

        public string? Text { get; set; }

        public string? Audio { get; internal set; }
    }
}