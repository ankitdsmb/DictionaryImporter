using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Gateway.Grammar.Correctors;

public sealed class LanguageToolGrammarCorrector(
    string languageToolUrl = "http://localhost:2026",
    ILogger<LanguageToolGrammarCorrector>? logger = null,
    IRewriteContextAccessor? rewriteContextAccessor = null,          // NEW (optional)
    IRewriteRuleHitRepository? rewriteRuleHitRepository = null)      // NEW (optional)
    : IGrammarCorrector
{
    // NOTE:
    // LanguageTool /v2/check expects application/x-www-form-urlencoded with:
    // 1) text=...
    // 2) language=en-US
    // Sending JSON causes: "Missing 'text' or 'data' parameter" (HTTP 400)

    private const int DefaultTimeoutSeconds = 30;

    // Safety: LanguageTool can fail on extremely long strings
    private const int MaxInputLength = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
    };

    private readonly string _languageToolUrl = languageToolUrl.TrimEnd('/');

    private readonly IRewriteContextAccessor? _rewriteContextAccessor = rewriteContextAccessor;
    private readonly IRewriteRuleHitRepository? _rewriteRuleHitRepository = rewriteRuleHitRepository;

    private readonly GrammarRuleFilter _ruleFilter = new()
    {
        SafeAutoCorrectRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MORFOLOGIK_RULE_EN_US",
            "MORFOLOGIK_RULE_EN_GB",
            "UPPERCASE_SENTENCE_START",
            "EN_A_VS_AN",
            "EN_CONTRACTION_SPELLING",
            "COMMA_PARENTHESIS_WHITESPACE",
            "DOUBLE_PUNCTUATION",
            "MISSING_COMMA",
            "EXTRA_SPACE"
        },
        HighConfidenceThreshold = 80,
        MaxSuggestionsPerIssue = 3
    };

    /// <summary>
    /// Checks grammar using LanguageTool server and returns filtered issues.
    /// This method NEVER throws (pipeline-safe). On error it returns no issues.
    /// </summary>
    public async Task<GrammarCheckResult> CheckAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);

        var sw = Stopwatch.StartNew();

        try
        {
            var normalizedText = NormalizeInput(text);
            var normalizedLanguage = NormalizeLanguage(languageCode);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = normalizedText,
                ["language"] = normalizedLanguage,
                ["enabledOnly"] = "false"
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_languageToolUrl}/v2/check")
            {
                Content = form
            };

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);

                logger?.LogWarning(
                    "LanguageTool check failed | Status={Status} | Language={Lang} | TextPreview={TextPreview} | Body={Body}",
                    (int)response.StatusCode,
                    normalizedLanguage,
                    normalizedText[..Math.Min(60, normalizedText.Length)],
                    body);

                return new GrammarCheckResult(false, 0, [], sw.Elapsed);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);

            var ltResponse = JsonSerializer.Deserialize<LanguageToolResponse>(responseJson, JsonOptions)
                             ?? new LanguageToolResponse();

            var issues = ConvertToIssues(ltResponse, normalizedText);

            var filteredIssues = FilterIssues(issues, forAutoCorrect: false);

            // NEW: track LanguageTool rule hits (best-effort, never throws)
            await TryTrackLanguageToolHitsAsync(filteredIssues, ct);

            return new GrammarCheckResult(
                filteredIssues.Count > 0,
                filteredIssues.Count,
                filteredIssues,
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "LanguageTool check failed for text: {Text}",
                text[..Math.Min(50, text.Length)]);

            return new GrammarCheckResult(false, 0, [], sw.Elapsed);
        }
        finally
        {
            sw.Stop();
        }
    }

    /// <summary>
    /// Applies safe auto-corrections based on rule allowlist + confidence threshold.
    /// Corrections are applied from end to start to preserve offsets.
    /// </summary>
    public async Task<GrammarCorrectionResult> AutoCorrectAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCorrectionResult(text, text, [], []);

        var checkResult = await CheckAsync(text, languageCode, ct);
        if (!checkResult.HasIssues)
            return new GrammarCorrectionResult(text, text, [], []);

        var safeIssues = checkResult.Issues
            .Where(issue =>
                _ruleFilter.SafeAutoCorrectRules.Contains(issue.RuleId) &&
                issue.ConfidenceLevel >= _ruleFilter.HighConfidenceThreshold)
            .OrderByDescending(i => i.StartOffset)
            .ToList();

        var correctedText = text;
        var appliedCorrections = new List<AppliedCorrection>();

        foreach (var issue in safeIssues)
        {
            if (issue.StartOffset < 0 || issue.EndOffset <= issue.StartOffset)
                continue;

            if (issue.EndOffset > correctedText.Length)
                continue;

            if (issue.Replacements.Count == 0)
                continue;

            var replacement = issue.Replacements[0];

            var length = issue.EndOffset - issue.StartOffset;
            var originalSegment = correctedText.Substring(issue.StartOffset, length);

            var updated = correctedText.Remove(issue.StartOffset, length)
                .Insert(issue.StartOffset, replacement);

            if (string.Equals(updated, correctedText, StringComparison.Ordinal))
                continue;

            correctedText = updated;

            appliedCorrections.Add(new AppliedCorrection(
                originalSegment,
                replacement,
                issue.RuleId,
                issue.Message,
                issue.ConfidenceLevel));
        }

        var remainingIssues = checkResult.Issues
            .Where(issue => !safeIssues.Contains(issue))
            .ToList();

        return new GrammarCorrectionResult(
            text,
            correctedText,
            appliedCorrections,
            remainingIssues);
    }

    /// <summary>
    /// Simple heuristic suggestions (non-AI, offline).
    /// </summary>
    public async Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        await Task.CompletedTask;

        var suggestions = new List<GrammarSuggestion>();

        if (!string.IsNullOrWhiteSpace(text) && text.Length > 50 && !text.Contains(","))
        {
            suggestions.Add(new GrammarSuggestion(
                text,
                "Consider adding commas for readability in longer sentences.",
                "Long sentences without punctuation can be hard to parse.",
                "clarity"));
        }

        if (!string.IsNullOrWhiteSpace(text) &&
            (text.Contains(" is ", StringComparison.OrdinalIgnoreCase) ||
             text.Contains(" was ", StringComparison.OrdinalIgnoreCase) ||
             text.Contains(" were ", StringComparison.OrdinalIgnoreCase)))
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.EndsWith("ed", StringComparison.OrdinalIgnoreCase) && w.Length > 3))
            {
                suggestions.Add(new GrammarSuggestion(
                    text,
                    "Consider using active voice for more direct statements.",
                    "Passive voice can reduce clarity and impact.",
                    "clarity"));
            }
        }

        return suggestions;
    }

    // ------------------------------------------------------------
    // NEW: Hit tracking
    // ------------------------------------------------------------

    // NEW METHOD (added)
    private async Task TryTrackLanguageToolHitsAsync(
        IReadOnlyList<GrammarIssue> issues,
        CancellationToken ct)
    {
        if (_rewriteRuleHitRepository is null)
            return;

        if (_rewriteContextAccessor is null)
            return;

        if (issues is null || issues.Count == 0)
            return;

        try
        {
            var ctx = _rewriteContextAccessor.Current;

            var sourceCode = NormalizeSource(ctx.SourceCode);
            var mode = MapMode(ctx.Mode);

            // Aggregate counts by RuleId (deterministic)
            var hitMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var issue in issues)
            {
                var ruleId = (issue.RuleId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(ruleId))
                    continue;

                // RuleKey column max is 400 chars (safe clamp)
                if (ruleId.Length > 400)
                    ruleId = ruleId[..400];

                if (hitMap.TryGetValue(ruleId, out var existing))
                    hitMap[ruleId] = existing + 1;
                else
                    hitMap[ruleId] = 1;
            }

            if (hitMap.Count == 0)
                return;

            var upserts = hitMap
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new RewriteRuleHitUpsert
                {
                    SourceCode = sourceCode,
                    Mode = mode,
                    RuleType = "LanguageTool",
                    RuleKey = kv.Key,
                    HitCount = kv.Value <= 0 ? 1 : kv.Value
                })
                .ToList();

            await _rewriteRuleHitRepository.UpsertHitsAsync(upserts, ct);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "LanguageTool hit tracking failed (ignored).");
        }
    }

    // NEW METHOD (added)
    private static string NormalizeSource(string? sourceCode)
        => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

    // NEW METHOD (added)
    private static string MapMode(RewriteTargetMode mode)
    {
        return mode switch
        {
            RewriteTargetMode.Definition => "Definition",
            RewriteTargetMode.Title => "Title",
            RewriteTargetMode.Example => "Example",
            _ => mode.ToString()
        };
    }

    // ------------------------------------------------------------
    // Internal helpers
    // ------------------------------------------------------------

    private static string NormalizeInput(string text)
    {
        text = (text ?? string.Empty).Trim();

        if (text.Length > MaxInputLength)
            text = text[..MaxInputLength];

        return text;
    }

    private static string NormalizeLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return "en-US";

        return languageCode.Trim();
    }

    private IReadOnlyList<GrammarIssue> ConvertToIssues(LanguageToolResponse response, string originalText)
    {
        var issues = new List<GrammarIssue>();

        foreach (var match in response.Matches ?? Enumerable.Empty<LanguageToolMatch>())
        {
            var start = match.Offset;
            var end = match.Offset + match.Length;

            if (start < 0) start = 0;
            if (end < start) end = start;
            if (start > originalText.Length) start = originalText.Length;
            if (end > originalText.Length) end = originalText.Length;

            var contextText = BuildContext(originalText, start, end);

            var categoryId = match.Rule?.Category?.Id ?? "LANGUAGETOOL";
            var normalizedCategory = NormalizeCategory(categoryId);

            issues.Add(new GrammarIssue(
                StartOffset: start,
                EndOffset: end,
                Message: match.Message ?? string.Empty,
                ShortMessage: normalizedCategory,
                Replacements: match.Replacements?
                    .Select(r => r.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Take(_ruleFilter.MaxSuggestionsPerIssue)
                    .ToList() ?? [],
                RuleId: match.Rule?.Id ?? "UNKNOWN_RULE",
                RuleDescription: match.Rule?.Category?.Name ?? "LanguageTool rule",
                Tags: BuildTags(match),
                Context: contextText,
                ContextOffset: 0,
                ConfidenceLevel: CalculateConfidence(match)
            ));
        }

        return issues;
    }

    // NEW METHOD (added)
    private static string NormalizeCategory(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return "LANGUAGETOOL";

        // LanguageTool sends categories like "TYPOS", "GRAMMAR", "STYLE", etc.
        return categoryId.Trim().ToUpperInvariant();
    }

    private static string BuildContext(string originalText, int start, int end)
    {
        if (string.IsNullOrEmpty(originalText))
            return string.Empty;

        const int contextWindow = 40;

        var left = Math.Max(0, start - contextWindow);
        var right = Math.Min(originalText.Length, end + contextWindow);

        return originalText.Substring(left, right - left);
    }

    private static List<string> BuildTags(LanguageToolMatch match)
    {
        var tags = new List<string>();

        var categoryId = match.Rule?.Category?.Id;
        if (!string.IsNullOrWhiteSpace(categoryId))
            tags.Add(NormalizeCategory(categoryId));

        tags.Add("languagetool");

        return tags;
    }

    private IReadOnlyList<GrammarIssue> FilterIssues(IReadOnlyList<GrammarIssue> issues, bool forAutoCorrect)
    {
        return issues
            .Where(issue => !_ruleFilter.ShouldIgnore(issue))
            .Where(issue => !forAutoCorrect || _ruleFilter.SafeAutoCorrectRules.Contains(issue.RuleId))
            .ToList();
    }

    private static int CalculateConfidence(LanguageToolMatch match)
    {
        var baseConfidence = 70;

        var cat = NormalizeCategory(match.Rule?.Category?.Id ?? string.Empty);

        if (cat.Equals("TYPOS", StringComparison.OrdinalIgnoreCase))
            baseConfidence = 95;
        else if (cat.Equals("GRAMMAR", StringComparison.OrdinalIgnoreCase))
            baseConfidence = 80;
        else if (cat.Equals("STYLE", StringComparison.OrdinalIgnoreCase))
            baseConfidence = 60;

        if (match.Replacements is { Count: > 0 })
            baseConfidence += 10;

        return Math.Min(100, baseConfidence);
    }

    // ------------------------------------------------------------
    // Filtering configuration
    // ------------------------------------------------------------

    private sealed class GrammarRuleFilter
    {
        public HashSet<string> SafeAutoCorrectRules { get; init; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int HighConfidenceThreshold { get; init; } = 80;

        public int MaxSuggestionsPerIssue { get; set; } = 3;

        public bool ShouldIgnore(GrammarIssue issue)
        {
            // Normalize to uppercase because LanguageTool categories are usually uppercase
            var ignoreCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CASING",
                "TYPOGRAPHY",
                "REDUNDANCY"
            };

            var ignoreRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "EN_UNPAIRED_BRACKETS",
                "WHITESPACE_RULE"
            };

            var category = GetCategoryFromIssue(issue);

            return ignoreCategories.Contains(category)
                   || ignoreRules.Contains(issue.RuleId)
                   || issue.ConfidenceLevel < 50;
        }

        private static string GetCategoryFromIssue(GrammarIssue issue)
        {
            // Prefer explicit category tags (if present)
            if (issue.Tags != null)
            {
                var firstCategory = issue.Tags.FirstOrDefault(t =>
                    t.Equals("TYPOS", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("GRAMMAR", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("STYLE", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("CASING", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("TYPOGRAPHY", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("REDUNDANCY", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("PUNCTUATION", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(firstCategory))
                    return firstCategory.ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(issue.ShortMessage))
                return issue.ShortMessage.Trim().ToUpperInvariant();

            return "UNKNOWN";
        }
    }

    #region LanguageTool API Models

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