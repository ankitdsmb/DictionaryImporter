namespace DictionaryImporter.Core.Grammar.Enhanced;

public sealed record GrammarPatternRule
{
    public GrammarPatternRule(string id, string pattern, string replacement, string description, string category, int confidence, List<string> languages)
    {
        Id = id;
        Pattern = pattern;
        Replacement = replacement;
        Description = description;
        Category = category;
        Confidence = confidence;
        Languages = languages;
    }

    public GrammarPatternRule(string id, string pattern, string replacement, string description, string category, int confidence, List<string> languages, int usageCount, int successCount)
    {
        Id = id;
        Pattern = pattern;
        Replacement = replacement;
        Description = description;
        Category = category;
        Confidence = confidence;
        Languages = languages;
        UsageCount = usageCount;
        SuccessCount = successCount;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Pattern { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "GRAMMAR";
    public int Confidence { get; set; } = 80;
    public List<string> Languages { get; set; } = ["en"];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int UsageCount { get; set; } = 0;
    public int SuccessCount { get; set; } = 0;

    public bool IsApplicable(string languageCode)
    {
        if (Languages.Contains("all", StringComparer.OrdinalIgnoreCase))
            return true;

        return Languages.Any(lang =>
            languageCode.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
    }
}