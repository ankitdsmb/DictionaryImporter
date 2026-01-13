using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DictionaryImporter.Core.Grammar.Enhanced
{
    public sealed class LanguageToolEngine : IGrammarEngine
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly ILogger<LanguageToolEngine> _logger;

        public string Name => "LanguageTool";
        public double ConfidenceWeight => 0.85;

        public LanguageToolEngine(string baseUrl, ILogger<LanguageToolEngine>? logger = null)
        {
            _baseUrl = baseUrl;
            _logger = logger;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public bool IsSupported(string languageCode) => true;

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

            try
            {
                var request = new { text, language = languageCode };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/v2/check", content, ct);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var ltResponse = JsonSerializer.Deserialize<LanguageToolResponse>(responseJson);

                var issues = ConvertToIssues(ltResponse);

                return new GrammarCheckResult(
                    issues.Any(),
                    issues.Count,
                    issues,
                    TimeSpan.Zero
                );
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "LanguageTool check failed");
                return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);
            }
        }

        public async Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode, CancellationToken ct)
        {
            var checkResult = await CheckAsync(text, languageCode, ct);

            if (!checkResult.HasIssues)
                return new GrammarCorrectionResult(text, text, Array.Empty<AppliedCorrection>(), Array.Empty<GrammarIssue>());

            var correctedText = text;
            var appliedCorrections = new List<AppliedCorrection>();

            foreach (var issue in checkResult.Issues.OrderByDescending(i => i.StartOffset))
            {
                if (issue.Replacements.Count == 0) continue;

                var originalSegment = correctedText.Substring(issue.StartOffset,
                    issue.EndOffset - issue.StartOffset);
                var replacement = issue.Replacements[0];

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

            return new GrammarCorrectionResult(
                text,
                correctedText,
                appliedCorrections,
                checkResult.Issues
            );
        }

        private List<GrammarIssue> ConvertToIssues(LanguageToolResponse response)
        {
            var issues = new List<GrammarIssue>();
            if (response?.Matches == null) return issues;

            foreach (var match in response.Matches)
            {
                issues.Add(new GrammarIssue(
                    match.Rule.Id,
                    match.Message,
                    match.Rule.Category.Name,
                    match.Offset,
                    match.Offset + match.Length,
                    match.Replacements?.Select(r => r.Value).ToList() ?? new(),
                    CalculateConfidence(match)
                ));
            }
            return issues;
        }

        private int CalculateConfidence(LanguageToolMatch match)
        {
            var baseConfidence = match.Rule.Category.Id switch
            {
                "TYPOS" => 95,
                "GRAMMAR" => 80,
                "STYLE" => 60,
                _ => 70
            };

            return match.Replacements?.Count > 0 ? Math.Min(100, baseConfidence + 10) : baseConfidence;
        }
    }

    // LanguageTool API models (add these)
    public sealed class LanguageToolResponse
    {
        [JsonPropertyName("matches")]
        public List<LanguageToolMatch>? Matches { get; set; }
    }

    public sealed class LanguageToolMatch
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("replacements")]
        public List<LanguageToolReplacement>? Replacements { get; set; }

        [JsonPropertyName("rule")]
        public LanguageToolRule Rule { get; set; } = new();
    }

    public sealed class LanguageToolReplacement
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }

    public sealed class LanguageToolRule
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public LanguageToolCategory Category { get; set; } = new();
    }

    public sealed class LanguageToolCategory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}