namespace DictionaryImporter.Gateway.Grammar.Core.Models;

public sealed record GrammarPatternRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Regex pattern to match
    public string Pattern { get; set; } = string.Empty;

    // Replacement string (Regex.Replace replacement syntax)
    public string Replacement { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // Categories help grouping + ordering
    // Example: GRAMMAR, STYLE, DICTIONARY, CLEANUP
    public string Category { get; set; } = "GRAMMAR";

    // Training/feedback confidence 0-100
    public int Confidence { get; set; } = 80;

    // Supported languages for this rule
    public List<string> Languages { get; set; } = ["en-US"];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int UsageCount { get; set; } = 0;
    public int SuccessCount { get; set; } = 0;

    // ✅ FIX: must be int to sort properly
    // Lower = runs earlier (higher priority)
    public int Priority { get; set; } = 100;

    // ✅ FIX: must be bool to enable/disable safely
    public bool Enabled { get; set; } = true;
}