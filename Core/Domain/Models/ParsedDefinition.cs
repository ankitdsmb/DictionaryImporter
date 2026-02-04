namespace DictionaryImporter.Core.Domain.Models;

public class ParsedDefinition
{
    public string? MeaningTitle { get; set; }
    public string? Definition { get; set; }
    public string? RawFragment { get; set; }
    public int SenseNumber { get; set; }
    public string? Domain { get; set; }
    public string? UsageLabel { get; set; }
    public string? Alias { get; set; }
    public IReadOnlyList<CrossReference>? CrossReferences { get; set; } = new List<CrossReference>();
    public IReadOnlyList<string>? Synonyms { get; set; } = new List<string>();
    public IReadOnlyList<string>? Examples { get; set; } = new List<string>();
    public long? ParentParsedId { get; set; }
    public bool HasNonEnglishText { get; set; }
    public long? NonEnglishTextId { get; set; }
    public string? SourceCode { get; set; }
    public string? PartOfSpeech { get; set; }
    public string Etymology { get; internal set; }
    public string Pronunciation { get; internal set; }
    public string DedupKey { get; internal set; }
    public string? IPA { get; internal set; }
    public string? GrammarInfo { get; internal set; }
    public string? UsageNote { get; internal set; }
}