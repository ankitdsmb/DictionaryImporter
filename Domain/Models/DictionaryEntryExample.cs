namespace DictionaryImporter.Domain.Models;

public class DictionaryEntryExample : TextContentEntity
{
    public long DictionaryEntryExampleId { get; set; }
    public long DictionaryEntryParsedId { get; set; }
    public string ExampleText { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }

    // Store original example when replaced with placeholder
    public string? OriginalExampleText { get; set; }
}