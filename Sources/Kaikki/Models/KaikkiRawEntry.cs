using Newtonsoft.Json;

namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiRawEntry
{
    [JsonProperty("word")]
    public string Word { get; set; } = null!;

    [JsonProperty("pos")]
    public string? PartOfSpeech { get; set; }

    [JsonProperty("lang")]
    public string LanguageCode { get; set; } = "en";

    [JsonProperty("senses")]
    public List<KaikkiSense> Senses { get; set; } = [];

    [JsonProperty("sounds")]
    public List<KaikkiSound> Sounds { get; set; } = [];

    [JsonProperty("etymology_text")]
    public string? EtymologyText { get; set; }

    [JsonProperty("etymology_templates")]
    public List<KaikkiEtymologyTemplate>? EtymologyTemplates { get; set; }

    [JsonProperty("derived")]
    public List<KaikkiDerived>? Derived { get; set; }

    [JsonProperty("forms")]
    public List<KaikkiForm>? Forms { get; set; }

    [JsonProperty("synonyms")]
    public List<KaikkiSynonym>? Synonyms { get; set; }

    [JsonProperty("antonyms")]
    public List<KaikkiSynonym>? Antonyms { get; set; }

    [JsonProperty("ipa")]
    public List<string>? Ipa { get; set; }
}