namespace DictionaryImporter.Core.Domain.Models;

public sealed class DictionaryEntrySynonym
{
    public long DictionaryEntrySynonymId { get; set; }
    public long DictionaryEntryParsedId { get; set; }
    public string SynonymText { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }
    public string SourceCode { get; internal set; }
}