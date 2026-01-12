// DictionaryImporter/Sources/Oxford/Models/OxfordRawEntry.cs

namespace DictionaryImporter.Sources.Oxford.Models;

public sealed class OxfordRawEntry
{
    public string Headword { get; set; } = null!;
    public List<OxfordSenseRaw> Senses { get; set; } = new();
    public string? Pronunciation { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? VariantForms { get; set; }
}