namespace DictionaryImporter.Domain.Models
{
    public sealed class DictionaryEntryEtymology
    {
        public long DictionaryEntryId { get; init; }
        public string EtymologyText { get; init; } = null!;
        public string? LanguageCode { get; init; }
        public DateTime CreatedUtc { get; init; }
        public string SourceCode { get; set; } = string.Empty;
        public bool HasNonEnglishText { get; set; }
        public bool IsBilingualText { get; set; }  // ✅ ADDED BACK
        public long? NonEnglishTextId { get; set; }
        public string? DetectedLanguages { get; set; }

    }
}