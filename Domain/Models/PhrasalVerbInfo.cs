namespace DictionaryImporter.Domain.Models;

/// <summary>
/// Information about phrasal verbs
/// </summary>
public class PhrasalVerbInfo
{
    public bool IsPhrasalVerb { get; set; }
    public string Verb { get; set; } = string.Empty;
    public string Particle { get; set; } = string.Empty;
    public IReadOnlyList<string> Patterns { get; set; } = new List<string>();
}