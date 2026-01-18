namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiRawEntry
    {
        public string Word { get; set; } = null!;

        public string? PartOfSpeech { get; set; }

        public string LanguageCode { get; set; } = "en";

        public List<KaikkiSense> Senses { get; set; } = [];

        public List<KaikkiSound> Sounds { get; set; } = [];

        public string? EtymologyText { get; set; }

        public List<KaikkiEtymologyTemplate>? EtymologyTemplates { get; set; }

        public List<KaikkiDerived>? Derived { get; set; }

        public List<KaikkiForm>? Forms { get; set; }

        public List<KaikkiSynonym>? Synonyms { get; set; }

        public List<KaikkiSynonym>? Antonyms { get; set; }

        public List<string>? Ipa { get; set; }

        public string? LangCode { get; set; } = "en";
        public string? Pos { get; internal set; }
    }
}