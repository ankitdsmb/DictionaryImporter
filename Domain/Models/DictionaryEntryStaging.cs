namespace DictionaryImporter.Domain.Models
{
    public class DictionaryEntryStaging
    {
        public string Word { get; set; } = null!;
        public string NormalizedWord { get; set; } = null!;
        public string? PartOfSpeech { get; set; }
        public string? Definition { get; set; }
        public string? Etymology { get; set; }
        public string? RawFragment { get; set; } // ← ADD THIS
        public int SenseNumber { get; set; } = 1;
        public string SourceCode { get; set; } = null!;
        public DateTime CreatedUtc { get; set; }
    }
}