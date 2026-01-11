namespace DictionaryImporter.Sources.StructuredJson.Models
{
    public sealed class StructuredJsonRawEntry
    {
        public string Word { get; set; } = null!;
        public string NormalizedWord { get; set; } = null!;
        public string Definition { get; set; } = null!;
        public string PartOfSpeech { get; set; } = null!;
        public int SenseNumber { get; set; }
    }
}
