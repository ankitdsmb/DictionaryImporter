namespace DictionaryImporter.Domain.Models;

public sealed class DictionaryEntry
{
    public long DictionaryEntryId { get; set; }
    public string Word { get; init; } = null!;
    public string NormalizedWord { get; init; } = null!;
    public string? PartOfSpeech { get; init; }
    public string Definition { get; init; } = null!;
    public string? Etymology { get; init; }
    public int SenseNumber { get; init; }
    public string SourceCode { get; init; } = null!;
    public DateTime CreatedUtc { get; init; }
}