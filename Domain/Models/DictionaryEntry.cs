namespace DictionaryImporter.Domain.Models;

public class DictionaryEntry
{
    public long DictionaryEntryId { get; set; }
    public string Word { get; set; } = null!;
    public string NormalizedWord { get; set; } = null!;
    public string? PartOfSpeech { get; set; }
    public string? Definition { get; set; }
    public string? Etymology { get; set; }
    public string? RawFragment { get; set; } // ← MUST EXIST
    public int SenseNumber { get; set; } = 1;
    public string SourceCode { get; set; } = null!;
    public long? CanonicalWordId { get; set; }
    public DateTime CreatedUtc { get; set; }
}