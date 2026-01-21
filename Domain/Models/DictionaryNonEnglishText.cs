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
    public abstract class TextContentEntity
    {
        public bool HasNonEnglishText { get; set; }
        public long? NonEnglishTextId { get; set; }
        public string SourceCode { get; set; } = null!;

        // Helper method to get the actual text (English or placeholder)
        public string GetDisplayText(string storedText, Func<long, string> nonEnglishTextLoader)
        {
            if (HasNonEnglishText && NonEnglishTextId.HasValue)
            {
                // Return placeholder from main table
                return storedText;
            }
            return storedText;
        }

        // Helper method to get original non-English text
        public string? GetOriginalNonEnglishText(Func<long, string> nonEnglishTextLoader)
        {
            if (HasNonEnglishText && NonEnglishTextId.HasValue)
            {
                return nonEnglishTextLoader(NonEnglishTextId.Value);
            }
            return null;
        }
    }

    // Update existing entities to inherit from TextContentEntity
    public class DictionaryEntryParsed : TextContentEntity
    {
        public long DictionaryEntryParsedId { get; set; }
        public long DictionaryEntryId { get; set; }
        public string MeaningTitle { get; set; } = null!;
        public string Definition { get; set; } = null!;
        public string RawFragment { get; set; } = null!;
        public int? SenseNumber { get; set; }
        public string? Domain { get; set; }
        public string? UsageLabel { get; set; }
        public string? Alias { get; set; }
        public long? ParentParsedId { get; set; }
        public DateTime CreatedUtc { get; set; }

        // Store original definition when replaced with placeholder
        public string? OriginalDefinition { get; set; }
    }

    public class DictionaryEntryExample : TextContentEntity
    {
        public long DictionaryEntryExampleId { get; set; }
        public long DictionaryEntryParsedId { get; set; }
        public string ExampleText { get; set; } = null!;
        public DateTime CreatedUtc { get; set; }

        // Store original example when replaced with placeholder
        public string? OriginalExampleText { get; set; }
    }

    // Similar updates for DictionaryEntrySynonym, DictionaryEntryEtymology, etc.
}