using Newtonsoft.Json;
using System.Collections.Generic;

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
    public List<KaikkiSense> Senses { get; set; } = new();

    [JsonProperty("sounds")]
    public List<KaikkiSound> Sounds { get; set; } = new();

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

public sealed class KaikkiSense
{
    [JsonProperty("glosses")]
    public List<string> Glosses { get; set; } = new();

    [JsonProperty("examples")]
    public List<KaikkiExample>? Examples { get; set; }

    [JsonProperty("categories")]
    public List<string>? Categories { get; set; }

    [JsonProperty("topics")]
    public List<string>? Topics { get; set; }

    [JsonProperty("tags")]
    public List<string>? Tags { get; set; }

    [JsonProperty("senseid")]
    public string? SenseId { get; set; }

    [JsonProperty("raw_glosses")]
    public List<string>? RawGlosses { get; set; }
}

public sealed class KaikkiExample
{
    [JsonProperty("text")]
    public string Text { get; set; } = null!;

    [JsonProperty("type")]
    public string? Type { get; set; }
}

public sealed class KaikkiSound
{
    [JsonProperty("ipa")]
    public string? Ipa { get; set; }

    [JsonProperty("tags")]
    public List<string>? Tags { get; set; }

    [JsonProperty("audio")]
    public string? AudioUrl { get; set; }

    [JsonProperty("ogg_url")]
    public string? OggUrl { get; set; }

    [JsonProperty("mp3_url")]
    public string? Mp3Url { get; set; }

    [JsonProperty("text")]
    public string? Text { get; set; }
}

public sealed class KaikkiEtymologyTemplate
{
    [JsonProperty("name")]
    public string Name { get; set; } = null!;

    [JsonProperty("args")]
    public Dictionary<string, string> Args { get; set; } = new();
}

public sealed class KaikkiDerived
{
    [JsonProperty("word")]
    public string Word { get; set; } = null!;

    [JsonProperty("sense")]
    public string? Sense { get; set; }
}

public sealed class KaikkiForm
{
    [JsonProperty("form")]
    public string Form { get; set; } = null!;

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();
}

public sealed class KaikkiSynonym
{
    [JsonProperty("word")]
    public string Word { get; set; } = null!;

    [JsonProperty("sense")]
    public string? Sense { get; set; }

    [JsonProperty("alt")]
    public string? Alternative { get; set; }
}