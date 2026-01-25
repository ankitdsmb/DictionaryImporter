namespace DictionaryImporter.Sources.Kaikki.Models;

public sealed class KaikkiExample
{
    public string? Text { get; set; }
    public string? Translation { get; set; }
    public string? Language { get; set; }
    public Dictionary<string, object>? RawData { get; set; }
}