// File: DictionaryImporter/Core/Grammar/Enhanced/LanguageToolEngine.cs
using System.Diagnostics;
using System.Text.Json;

namespace DictionaryImporter.Core.Grammar.Enhanced;

public sealed class LanguageToolEngine : IGrammarEngine
{
    private readonly string _languageToolUrl;
    private readonly ILogger<LanguageToolEngine> _logger;
    private readonly HttpClient _httpClient;

    public string Name => "LanguageTool";
    public double ConfidenceWeight => 0.85;

    public LanguageToolEngine(string languageToolUrl, ILogger<LanguageToolEngine> logger)
    {
        _languageToolUrl = languageToolUrl ?? throw new ArgumentNullException(nameof(languageToolUrl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public Task InitializeAsync()
    {
        // LanguageTool is HTTP-based, no local initialization needed
        return Task.CompletedTask;
    }

    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        try
        {
            var request = new
            {
                text = text,
                language = languageCode,
                enabledOnly = false
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_languageToolUrl}/v2/check",
                content,
                ct);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<LanguageToolResponse>(responseJson);

                if (result?.matches != null)
                {
                    foreach (var match in result.matches)
                    {
                        var replacements = match.replacements?
                            .Select(r => r.value)
                            .ToList() ?? new List<string>();

                        var grammarIssue = new GrammarIssue(
                            StartOffset: match.offset,
                            EndOffset: match.offset + match.length,
                            Message: match.message,
                            ShortMessage: GetShortMessageFromRule(match.rule),
                            Replacements: replacements,
                            RuleId: match.rule.id,
                            RuleDescription: match.rule.description,
                            Tags: GetTagsFromLanguageToolRule(match.rule),
                            Context: GetContext(text, match.offset),
                            ContextOffset: Math.Max(0, match.offset - 20),
                            ConfidenceLevel: CalculateConfidenceFromMatch(match)
                        );

                        issues.Add(grammarIssue);
                    }
                }
            }
            else
            {
                _logger.LogWarning("LanguageTool API returned {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling LanguageTool API");
        }

        sw.Stop();
        return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    }

    private string GetContext(string text, int offset, int contextLength = 50)
    {
        var start = Math.Max(0, offset - contextLength);
        var end = Math.Min(text.Length, offset + contextLength);
        return text.Substring(start, end - start);
    }

    private string GetShortMessageFromRule(LanguageToolRule rule)
    {
        if (!string.IsNullOrEmpty(rule.category?.name))
            return rule.category.name;

        if (!string.IsNullOrEmpty(rule.issueType?.typeName))
            return rule.issueType.typeName;

        return rule.id.Split('_').FirstOrDefault() ?? "grammar";
    }

    private IReadOnlyList<string> GetTagsFromLanguageToolRule(LanguageToolRule rule)
    {
        var tags = new List<string>();

        // Add category as tag
        if (!string.IsNullOrEmpty(rule.category?.name))
            tags.Add(rule.category.name.ToLowerInvariant());

        // Add rule type
        if (!string.IsNullOrEmpty(rule.issueType?.typeName))
            tags.Add(rule.issueType.typeName.ToLowerInvariant());

        // Add default tag
        if (tags.Count == 0)
            tags.Add("language-tool");

        return tags;
    }

    private int CalculateConfidenceFromMatch(LanguageToolMatch match)
    {
        // Calculate confidence based on rule quality score
        if (match.rule?.issueType?.qualityScore != null)
            return (int)(match.rule.issueType.qualityScore * 100);

        // Default confidence levels based on rule type
        return match.rule?.issueType?.typeName?.ToLowerInvariant() switch
        {
            "typographical" => 95,
            "grammar" => 90,
            "style" => 80,
            "spelling" => 99,
            _ => 85
        };
    }

    public bool IsSupported(string languageCode)
    {
        // LanguageTool supports many languages
        return languageCode switch
        {
            "en-US" or "en-GB" or "en-CA" or "en-AU" => true,
            "de-DE" or "de-AT" or "de-CH" => true,
            "fr-FR" or "fr-CA" => true,
            "es-ES" or "es-MX" => true,
            "pt-PT" or "pt-BR" => true,
            "it-IT" => true,
            "nl-NL" => true,
            "pl-PL" => true,
            "ru-RU" => true,
            _ => false
        };
    }

    // LanguageTool API response models
    private class LanguageToolResponse
    {
        public List<LanguageToolMatch>? matches { get; set; }
    }

    private class LanguageToolMatch
    {
        public int offset { get; set; }
        public int length { get; set; }
        public string message { get; set; } = null!;
        public string? shortMessage { get; set; }
        public List<LanguageToolReplacement>? replacements { get; set; }
        public LanguageToolRule rule { get; set; } = null!;
    }

    private class LanguageToolReplacement
    {
        public string value { get; set; } = null!;
    }

    private class LanguageToolRule
    {
        public string id { get; set; } = null!;
        public string description { get; set; } = null!;
        public LanguageToolCategory? category { get; set; }
        public LanguageToolIssueType? issueType { get; set; }
    }

    private class LanguageToolCategory
    {
        public string name { get; set; } = null!;
    }

    private class LanguageToolIssueType
    {
        public string typeName { get; set; } = null!;
        public double? qualityScore { get; set; }
    }
}