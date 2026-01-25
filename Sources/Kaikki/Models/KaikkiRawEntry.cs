namespace DictionaryImporter.Sources.Kaikki.Models;

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

    // Based on other raw entry models in the codebase, it likely has:
    public string Headword { get; init; } = null!;

    public string RawLine { get; init; } = null!; // This contains the JSON data

    // Or it might have specific fields extracted from JSON
    public List<string> RawIpaLines { get; set; } = new();

    public List<ParsedIpa> ParsedIpa { get; set; } = new();
    public string? Etymology { get; set; }
    public string? RawJson { get; internal set; }
}