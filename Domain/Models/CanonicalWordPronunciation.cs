namespace DictionaryImporter.Domain.Models
{
    public sealed class CanonicalWordPronunciation
    {
        public long CanonicalWordId { get; set; }
        public string LocaleCode { get; set; } = null!;
        public string Ipa { get; set; } = null!;
        public DateTime CreatedUtc { get; set; }
    }
}