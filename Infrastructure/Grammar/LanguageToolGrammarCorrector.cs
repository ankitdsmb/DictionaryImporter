using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.Infrastructure.Grammar;

public sealed class LanguageToolGrammarCorrector(
    string languageToolUrl = "http://localhost:8081",
    ILogger<LanguageToolGrammarCorrector>? logger = null)
    : IGrammarCorrector
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _languageToolUrl = languageToolUrl.TrimEnd('/');

    private readonly GrammarRuleFilter _ruleFilter = new()
    {
        SafeAutoCorrectRules =
        [
            "MORFOLOGIK_RULE_EN_US", "MORFOLOGIK_RULE_EN_GB",
            "UPPERCASE_SENTENCE_START",
            "EN_A_VS_AN",
            "EN_CONTRACTION_SPELLING",
            "COMMA_PARENTHESIS_WHITESPACE",
            "DOUBLE_PUNCTUATION",
            "MISSING_COMMA",
            "EXTRA_SPACE"
        ],
        HighConfidenceThreshold = 80,
        MaxSuggestionsPerIssue = 3
    };

    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var request = new LanguageToolRequest
            {
                Text = text,
                Language = languageCode,
                EnabledOnly = false
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_languageToolUrl}/v2/check", content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var ltResponse = JsonSerializer.Deserialize<LanguageToolResponse>(responseJson);

            var issues = ConvertToIssues(ltResponse);
            var filteredIssues = FilterIssues(issues, false);

            sw.Stop();

            return new GrammarCheckResult(
                filteredIssues.Count > 0,
                filteredIssues.Count,
                filteredIssues,
                sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "LanguageTool check failed for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));
            return new GrammarCheckResult(false, 0, [], sw.Elapsed);
        }
    }

    public async Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCorrectionResult(text, text, [], []);

        var checkResult = await CheckAsync(text, languageCode, ct);
        if (!checkResult.HasIssues)
            return new GrammarCorrectionResult(text, text, [], []);

        var safeIssues = checkResult.Issues
            .Where(issue => _ruleFilter.SafeAutoCorrectRules.Contains(issue.RuleId) &&
                   issue.ConfidenceLevel >= _ruleFilter.HighConfidenceThreshold)
            .OrderByDescending(i => i.StartOffset)
            .ToList();

        var correctedText = text;
        var appliedCorrections = new List<AppliedCorrection>();

        foreach (var issue in safeIssues)
        {
            if (issue.Replacements.Count == 0)
                continue;

            var replacement = issue.Replacements[0];

            var originalSegment = correctedText.Substring(issue.StartOffset,
                issue.EndOffset - issue.StartOffset);

            correctedText = correctedText.Remove(issue.StartOffset,
                issue.EndOffset - issue.StartOffset)
                .Insert(issue.StartOffset, replacement);

            appliedCorrections.Add(new AppliedCorrection(
                originalSegment,
                replacement,
                issue.RuleId,
                issue.Message,
                issue.ConfidenceLevel
            ));
        }

        var remainingIssues = checkResult.Issues
            .Where(issue => !safeIssues.Contains(issue))
            .ToList();

        return new GrammarCorrectionResult(
            text,
            correctedText,
            appliedCorrections,
            remainingIssues
        );
    }

    public async Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        var suggestions = new List<GrammarSuggestion>();

        if (text.Length > 50 && !text.Contains(","))
        {
            suggestions.Add(new GrammarSuggestion(
                text,
                "Consider adding commas for readability in longer sentences.",
                "Long sentences without punctuation can be hard to parse.",
                "clarity"
            ));
        }

        if (text.Contains(" is ") || text.Contains(" was ") || text.Contains(" were "))
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.EndsWith("ed") && w.Length > 3))
            {
                suggestions.Add(new GrammarSuggestion(
                    text,
                    "Consider using active voice for more direct statements.",
                    "Passive voice can reduce clarity and impact.",
                    "clarity"
                ));
            }
        }

        return suggestions;
    }

    private IReadOnlyList<GrammarIssue> ConvertToIssues(LanguageToolResponse response)
    {
        var issues = new List<GrammarIssue>();

        foreach (var match in response.Matches ?? Enumerable.Empty<LanguageToolMatch>())
        {
            issues.Add(new GrammarIssue(
                StartOffset: match.Offset,
                EndOffset: Math.Min(50, match.Length),
                Message: match.Message,
                ShortMessage: "Language detection",
                Replacements: match.Replacements?.Select(r => r.Value).ToList() ?? [],
                RuleId: match.Rule.Id,
                RuleDescription: "Language content detection",
                Tags: new List<string> { "language", "detection" }, Context: match.Message.Substring(0, Math.Min(100, match.Length)),
                ContextOffset: 0,
                ConfidenceLevel: CalculateConfidence(match)
            ));
        }

        return issues;
    }

    private IReadOnlyList<GrammarIssue> FilterIssues(IReadOnlyList<GrammarIssue> issues, bool forAutoCorrect)
    {
        return issues
            .Where(issue => !_ruleFilter.ShouldIgnore(issue))
            .Where(issue => !forAutoCorrect || _ruleFilter.SafeAutoCorrectRules.Contains(issue.RuleId))
            .ToList();
    }

    private int CalculateConfidence(LanguageToolMatch match)
    {
        var baseConfidence = 70;

        if (match.Rule.Category.Id == "TYPOS")
            baseConfidence = 95;
        else if (match.Rule.Category.Id == "GRAMMAR")
            baseConfidence = 80;
        else if (match.Rule.Category.Id == "STYLE")
            baseConfidence = 60;

        if (match.Replacements?.Count > 0)
            baseConfidence += 10;

        return Math.Min(100, baseConfidence);
    }

    private sealed class GrammarRuleFilter
    {
        public HashSet<string> SafeAutoCorrectRules { get; init; } = [];
        public int HighConfidenceThreshold { get; init; } = 80;
        public int MaxSuggestionsPerIssue { get; set; } = 3;

        public bool ShouldIgnore(GrammarIssue issue)
        {
            var ignoreCategories = new[] { "CASING", "TYPOGRAPHY", "REDUNDANCY" };
            var ignoreRules = new[] { "EN_UNPAIRED_BRACKETS", "WHITESPACE_RULE" };

            var category = GetCategoryFromIssue(issue);
            return ignoreCategories.Contains(category.ToUpper()) || ignoreRules.Contains(issue.RuleId) || issue.ConfidenceLevel < 50;
        }

        private string GetCategoryFromIssue(GrammarIssue issue)
        {
            var categoryTags = issue.Tags?
                .Where(t => t.Contains("grammar") || t.Contains("spelling") || t.Contains("style"))
                .ToList();

            if (categoryTags?.Count > 0)
                return categoryTags.First();

            if (!string.IsNullOrEmpty(issue.ShortMessage))
                return issue.ShortMessage;

            if (issue.RuleId.StartsWith("SPELLING_"))
                return "spelling";
            if (issue.RuleId.StartsWith("PATTERN_"))
                return "pattern";
            if (issue.RuleId.StartsWith("GRAMMAR_"))
                return "grammar";

            return "unknown";
        }
    }

    #region LanguageTool API Models

    private sealed class LanguageToolRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = "en-US";

        [JsonPropertyName("enabledOnly")]
        public bool EnabledOnly { get; set; } = false;
    }

    private sealed class LanguageToolResponse
    {
        [JsonPropertyName("matches")]
        public List<LanguageToolMatch> Matches { get; init; } = [];
    }

    private sealed class LanguageToolMatch
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("replacements")]
        public List<LanguageToolReplacement> Replacements { get; set; } = [];

        [JsonPropertyName("rule")]
        public LanguageToolRule Rule { get; set; } = new();
    }

    private sealed class LanguageToolReplacement
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class LanguageToolRule
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public LanguageToolCategory Category { get; set; } = new();
    }

    private sealed class LanguageToolCategory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    #endregion LanguageTool API Models
}