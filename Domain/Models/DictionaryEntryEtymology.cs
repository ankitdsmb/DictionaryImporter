namespace DictionaryImporter.Domain.Models
{
    public sealed class DictionaryEntryEtymology
    {
        public long DictionaryEntryId { get; init; }
        public string EtymologyText { get; init; } = null!;
        public string? LanguageCode { get; init; }
        public DateTime CreatedUtc { get; init; }
    }
}