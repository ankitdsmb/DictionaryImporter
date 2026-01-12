namespace DictionaryImporter.Sources.Oxford.Models;

public sealed class OxfordSenseRaw
{
    public int SenseNumber { get; set; }
    public string? SenseLabel { get; set; } // e.g., "(informal, chiefly N. Amer.)"
    public string Definition { get; set; } = null!;
    public string? ChineseTranslation { get; set; } // After "•"
    public List<string> Examples { get; set; } = new();
    public string? UsageNote { get; set; }
    public List<string> CrossReferences { get; set; } = new();
}