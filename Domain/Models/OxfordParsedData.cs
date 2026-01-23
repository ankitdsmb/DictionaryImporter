namespace DictionaryImporter.Domain.Models;

public class OxfordParsedData
{
    public string? Domain { get; set; }
    public string? IpaPronunciation { get; set; }
    public string? PartOfSpeech { get; set; }
    public IReadOnlyList<string> Variants { get; set; } = new List<string>();
    public string? UsageLabel { get; set; }
    public string CleanDefinition { get; set; } = string.Empty;
}