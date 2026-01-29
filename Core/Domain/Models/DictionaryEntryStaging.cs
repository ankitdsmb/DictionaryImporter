namespace DictionaryImporter.Core.Domain.Models;

public class DictionaryEntryStaging
{
    public string Word { get; set; } = null!;
    public string NormalizedWord { get; set; } = null!;
    public string? PartOfSpeech { get; set; }
    public string? Definition { get; set; }
    public string? Etymology { get; set; }
    public string? RawFragment { get; set; }
    public int SenseNumber { get; set; } = 1;
    public string SourceCode { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }

    // EXISTING (do NOT change)
    public string DedupKeyHash { get; internal set; } = null!;

    public string DefinitionHash { get; internal set; } = null!;

    // ✅ NEW (for DB only)
    public byte[] WordHashBytes { get; internal set; } = null!;

    public byte[] DefinitionHashBytes { get; internal set; } = null!;
}