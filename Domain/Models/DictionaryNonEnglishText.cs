namespace DictionaryImporter.Domain.Models
{
    public class DictionaryNonEnglishText
    {
        public long NonEnglishTextId { get; set; }
        public string OriginalText { get; set; } = null!;
        public string? DetectedLanguage { get; set; }
        public int CharacterCount { get; set; }
        public string SourceCode { get; set; } = null!;
        public DateTime CreatedUtc { get; set; }
        public string? TextPreview { get; set; } // Computed column
    }

    // Base class for entities that can have non-English text

    // Update existing entities to inherit from TextContentEntity

    // Similar updates for DictionaryEntrySynonym, DictionaryEntryEtymology, etc.
}