namespace DictionaryImporter.Domain.Models;

public class Century21ParsedData
{
    public string Headword { get; set; } = string.Empty;
    public string? IpaPronunciation { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? GrammarInfo { get; set; }
    public IReadOnlyList<string> Definitions { get; set; } = new List<string>();
    public IReadOnlyList<string> Examples { get; set; } = new List<string>();
    public IReadOnlyList<Century21VariantData> Variants { get; set; } = new List<Century21VariantData>();
    public IReadOnlyList<Century21IdiomData> Idioms { get; set; } = new List<Century21IdiomData>();
}