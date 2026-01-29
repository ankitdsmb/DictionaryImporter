// File: Domain/Models/ParsedDefinition.cs
namespace DictionaryImporter.Core.Domain.Models;

public class ParsedDefinition
{
    public long? DictionaryEntryParsedId { get; set; }
    public long DictionaryEntryId { get; set; }
    public string? MeaningTitle { get; set; }
    public string? Definition { get; set; }
    public string? RawFragment { get; set; }
    public int SenseNumber { get; set; }
    public string? Domain { get; set; }
    public string? UsageLabel { get; set; }
    public string? Alias { get; set; }

    // FIXED: Change to IReadOnlyList
    public IReadOnlyList<CrossReference>? CrossReferences { get; set; } = new List<CrossReference>();

    public IReadOnlyList<string>? Synonyms { get; set; } = new List<string>();
    public IReadOnlyList<string>? Examples { get; set; } = new List<string>();

    // Add this property for parent relationship
    public long? ParentParsedId { get; set; }

    // Add these properties for non-English text handling
    public bool HasNonEnglishText { get; set; }

    public long? NonEnglishTextId { get; set; }
    public string? SourceCode { get; set; }

    // Helper properties
    public string? SelfKey { get; set; }

    public string? ParentKey { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? DetectedLanguages { get; set; }
    public string Etymology { get; internal set; }
    public double? PartOfSpeechConfidence { get; internal set; }
    public string Pronunciation { get; internal set; }
    public string DedupKey { get; internal set; }
    public string IPA { get; internal set; }
    public string GrammarInfo { get; internal set; }
    public string UsageNote { get; internal set; }
}