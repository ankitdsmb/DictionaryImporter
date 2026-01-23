namespace DictionaryImporter.Sources.Common.Helper;

public class Century21VariantData
{
    public string? PartOfSpeech { get; set; }
    public IReadOnlyList<string> Definitions { get; set; } = new List<string>();
    public IReadOnlyList<string> Examples { get; set; } = new List<string>();
    public string? GrammarInfo { get; set; }
}