namespace DictionaryImporter.AITextKit.Grammar.Core.Models;

public sealed record GrammarPatternRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Pattern { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "GRAMMAR";
    public int Confidence { get; set; } = 80;
    public List<string> Languages { get; set; } = ["en-US"];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int UsageCount { get; set; } = 0;
    public int SuccessCount { get; set; } = 0;
}