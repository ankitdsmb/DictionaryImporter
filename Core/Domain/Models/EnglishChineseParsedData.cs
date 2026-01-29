namespace DictionaryImporter.Core.Domain.Models;

public class EnglishChineseParsedData
{
    public string Headword { get; set; } = string.Empty;
    public string? Syllabification { get; set; }
    public string? IpaPronunciation { get; set; }
    public string? PartOfSpeech { get; set; }
    public string MainDefinition { get; set; } = string.Empty;
    public IReadOnlyList<string> DomainLabels { get; set; } = new List<string>();
    public IReadOnlyList<string> RegisterLabels { get; set; } = new List<string>();
    public string? Etymology { get; set; }
    public IReadOnlyList<string> Examples { get; set; } = new List<string>();
    public IReadOnlyList<EnglishChineseParsedData> AdditionalSenses { get; set; } = new List<EnglishChineseParsedData>();
    public int SenseNumber { get; set; } = 1;
    public string EnglishDefinition { get; internal set; }
}