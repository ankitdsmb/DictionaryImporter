using System.Text.Json;
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

public sealed class GrammarFeedback
{
    public string OriginalText { get; set; } = null!;
    public string? CorrectedText { get; set; }
    public GrammarIssue? OriginalIssue { get; set; }
    public bool IsValidCorrection { get; set; }
    public bool IsFalsePositive { get; set; }
    public string? UserComment { get; set; }

    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset Timestamp { get; set; }
}

// Helper converter for DateTimeOffset serialization
public class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTimeOffset.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("O"));
    }
}

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