namespace DictionaryImporter.Core.Persistence;

public sealed class AiDefinitionCandidate
{
    public long ParsedDefinitionId { get; set; }
    public string DefinitionText { get; set; } = string.Empty;
}