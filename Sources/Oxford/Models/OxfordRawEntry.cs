// DictionaryImporter/Sources/Oxford/Models/OxfordRawEntry.cs
namespace DictionaryImporter.Sources.Oxford.Models
{
    public sealed class OxfordRawEntry
    {
        public string Headword { get; set; } = null!;
        public List<OxfordSenseRaw> Senses { get; set; } = new();
        public string? Pronunciation { get; set; }
        public string? PartOfSpeech { get; set; }
        public string? VariantForms { get; set; }
    }

    public sealed class OxfordSenseRaw
    {
        public int SenseNumber { get; set; }
        public string? SenseLabel { get; set; } // e.g., "(informal, chiefly N. Amer.)"
        public string Definition { get; set; } = null!;
        public string? ChineseTranslation { get; set; } // After "•"
        public List<string> Examples { get; set; } = new();
        public string? UsageNote { get; set; }
        public List<string> CrossReferences { get; set; } = new();
    }
}