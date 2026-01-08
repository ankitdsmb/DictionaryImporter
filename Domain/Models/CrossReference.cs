namespace DictionaryImporter.Domain.Models
{
    public sealed class CrossReference
    {
        public string TargetWord { get; set; } = null!;
        public string ReferenceType { get; set; } = null!; // See | SeeAlso | Cf
    }
}
