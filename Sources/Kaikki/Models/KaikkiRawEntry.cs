namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiRawEntry
    {
        public string Word { get; set; } = null!;
        public string? Pos { get; set; }
        public string? EtymologyText { get; set; }
        public List<KaikkiSound> Sounds { get; set; } = [];
        public List<KaikkiSense> Senses { get; set; } = [];
        public List<KaikkiForm> Forms { get; set; } = [];
        public List<KaikkiTranslation> Translations { get; set; } = [];
        public List<KaikkiHeadTemplate> HeadTemplates { get; set; } = [];
        public List<string> Hyphenations { get; set; } = [];
        public string? LangCode { get; set; } = "en";
    }
}