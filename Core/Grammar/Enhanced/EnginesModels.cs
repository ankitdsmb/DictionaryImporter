using System.Text.Json.Serialization;

namespace DictionaryImporter.Core.Grammar.Enhanced;

#region Engine Models

public sealed record EngineSuggestion(
    string EngineName,
    string SuggestedText,
    double Confidence,
    string RuleId,
    string Explanation
);

public sealed record GrammarFeedback(
    string OriginalText,
    string? CorrectedText,
    GrammarIssue? OriginalIssue,
    bool IsValidCorrection,
    bool IsFalsePositive,
    string? UserComment,
    DateTimeOffset Timestamp
);

public sealed record GrammarPatternRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Pattern { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "GRAMMAR";
    public int Confidence { get; set; } = 80;
    public List<string> Languages { get; set; } = new() { "en" };
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int UsageCount { get; set; } = 0;
    public int SuccessCount { get; set; } = 0;

    public bool IsApplicable(string languageCode)
    {
        if (Languages.Contains("all")) return true;
        return Languages.Any(lang => languageCode.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
    }
}

#endregion Engine Models