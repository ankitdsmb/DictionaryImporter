namespace DictionaryImporter.Sources.Oxford.Models;

public sealed class OxfordRawEntry
{
    public string Headword { get; set; } = null!;
    public List<OxfordSenseRaw> Senses { get; set; } = [];
    public string? Pronunciation { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? VariantForms { get; set; }
}