namespace DictionaryImporter.Sources.Oxford.Models
{
    public sealed class OxfordSenseRaw
    {
        public int SenseNumber { get; set; }
        public string? SenseLabel { get; set; }
        public string Definition { get; set; } = null!;
        public string? ChineseTranslation { get; set; }
        public List<string> Examples { get; set; } = [];
        public string? UsageNote { get; set; }
        public List<string> CrossReferences { get; set; } = [];
    }
}