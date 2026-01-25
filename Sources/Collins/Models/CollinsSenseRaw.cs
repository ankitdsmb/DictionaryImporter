namespace DictionaryImporter.Sources.Collins.Models;

public sealed class CollinsSenseRaw
{
    public int SenseNumber { get; set; }

    public string PartOfSpeech { get; set; } = null!;

    public string Definition { get; set; } = null!;

    public List<string> Synonyms { get; } = [];
    public List<string> Examples { get; } = [];
    public string? GrammarInfo { get; set; }
    public string? DomainLabel { get; set; }
    public string? UsageNote { get; set; }
}