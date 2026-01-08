namespace DictionaryImporter.Domain.Models
{
    public sealed class DictionaryEntryCrossReference
    {
        public long DictionaryEntryCrossReferenceId { get; set; }
        public long SourceParsedId { get; set; }
        public string TargetWord { get; set; } = null!;
        public string ReferenceType { get; set; } = null!;
        public string? SourceCode { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}