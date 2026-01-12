namespace DictionaryImporter.Sources.Collins.Models;

public sealed class CollinsSenseRaw
{
    public int SenseNumber { get; set; }

    public string PartOfSpeech { get; set; } = null!;

    public string Definition { get; set; } = null!;

    //public string? UsageLabel { get; set; }

    //public string? Domain { get; set; }

    public List<string> Synonyms { get; } = new();
    public List<string> Examples { get; } = new();
    public string? GrammarInfo { get; set; }
    public string? DomainLabel { get; set; }
    public string? UsageNote { get; set; }
}