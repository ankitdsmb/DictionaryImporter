namespace DictionaryImporter.Domain.Models;

public sealed class ParsedDefinition
{
    public string? ParentKey { get; set; }

    public string? SelfKey { get; set; }

    public string MeaningTitle { get; set; } = null!;

    public int? SenseNumber { get; set; }
    public string Definition { get; set; } = null!;
    public string RawFragment { get; set; } = null!;

    public string? Domain { get; set; }

    public string? UsageLabel { get; set; }
    public string? Alias { get; set; }

    public IReadOnlyList<string>? Synonyms { get; set; }

    public IReadOnlyList<CrossReference> CrossReferences { get; set; }
        = new List<CrossReference>();

    public string? PartOfSpeech { get; set; }
    public List<string> Examples { get; internal set; }
}