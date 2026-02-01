namespace DictionaryImporter.Gateway.Grammar.Core.Models;

public sealed class GrammarOptions
{
    public bool Enabled { get; set; }

    public string LanguageToolUrl { get; set; } = "http://localhost:2026";
    public string DefaultLanguage { get; set; } = "en-US";

    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RateLimitPerMinute { get; set; } = 60;

    public int CacheMinutes { get; set; } = 10;
    public int CommonTextCacheHours { get; set; } = 1;

    public List<string> SafeAutoCorrectRules { get; set; } = new();
}