namespace DictionaryImporter.Core.Domain.Models;

/// <summary>
/// Structured data container for Collins parsing results
/// </summary>
public class CollinsParsedData
{
    public int SenseNumber { get; set; } = 1;
    public string PartOfSpeech { get; set; } = "unk";
    public string ChinesePartOfSpeech { get; set; } = string.Empty;
    public string MainDefinition { get; set; } = string.Empty;
    public string RawDefinitionStart { get; set; } = string.Empty;
    public string CleanDefinition { get; set; } = string.Empty;
    public IReadOnlyList<string> DomainLabels { get; set; } = new List<string>();
    public IReadOnlyList<string> UsagePatterns { get; set; } = new List<string>();
    public IReadOnlyList<string> Examples { get; set; } = new List<string>();
    public IReadOnlyList<CrossReference> CrossReferences { get; set; } = new List<CrossReference>();
    public PhrasalVerbInfo PhrasalVerbInfo { get; set; } = new PhrasalVerbInfo();
    public bool IsPhrasalVerb { get; set; }

    //public string PrimaryDomain => DomainLabels?.FirstOrDefault() ?? string.Empty;
    public string PrimaryDomain { get; set; } = string.Empty;

    public string PrimaryUsagePattern => UsagePatterns?.FirstOrDefault() ?? string.Empty;

    public List<string>? Synonyms { get; internal set; }
    public string Alias { get; internal set; }
    public string IPA { get; internal set; }
    public string GrammarInfo { get; internal set; }
    public string UsageNote { get; internal set; }
}