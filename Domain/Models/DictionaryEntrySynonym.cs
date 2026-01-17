namespace DictionaryImporter.Domain.Models
{
    public sealed class DictionaryEntrySynonym
    {
        public long DictionaryEntrySynonymId { get; set; }
        public long DictionaryEntryParsedId { get; set; }
        public string SynonymText { get; set; } = null!;
        public string? Source { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}