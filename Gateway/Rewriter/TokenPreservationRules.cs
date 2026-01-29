namespace DictionaryImporter.Gateway.Rewriter;

public sealed class TokenPreservationRules
{
    public HashSet<string> AlwaysPreserveExact { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> ProperNouns { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> ProtectedPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ProtectedSuffixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RegexPatterns { get; set; } = new();
    public List<string> WordBoundaryPatterns { get; set; } = new();
}