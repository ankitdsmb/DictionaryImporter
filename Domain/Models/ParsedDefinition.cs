namespace DictionaryImporter.Domain.Models;

public sealed class ParsedDefinition
{
    // --------------------------------------------------
    // HIERARCHY (INTENT ONLY — NO DB IDS)
    // --------------------------------------------------
    public string? ParentKey { get; set; }

    public string? SelfKey { get; set; }

    // --------------------------------------------------
    // CORE MEANING
    // --------------------------------------------------
    public string MeaningTitle { get; set; } = null!;

    public int? SenseNumber { get; set; }
    public string Definition { get; set; } = null!;
    public string RawFragment { get; set; } = null!;

    // --------------------------------------------------
    // LINGUISTIC ANNOTATIONS
    // --------------------------------------------------
    public string? Domain { get; set; }

    public string? UsageLabel { get; set; }
    public string? Alias { get; set; }

    // --------------------------------------------------
    // RELATIONS
    // --------------------------------------------------
    public IReadOnlyList<string>? Synonyms { get; set; }

    public IReadOnlyList<CrossReference> CrossReferences { get; set; }
        = new List<CrossReference>();

    public string? PartOfSpeech { get; set; }
    public List<string> Examples { get; internal set; }
}