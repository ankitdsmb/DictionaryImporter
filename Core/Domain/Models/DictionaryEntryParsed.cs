namespace DictionaryImporter.Core.Domain.Models;

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