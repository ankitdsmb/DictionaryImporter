// File: DictionaryImporter/Infrastructure/Grammar/Engines/GrammarNetEngine.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using System.Diagnostics;
using System.Text.Json;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class GrammarNetEngine : IGrammarEngine
{
    private readonly ILogger<GrammarNetEngine> _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public string Name => "GrammarNet";
    public double ConfidenceWeight => 0.85;

    public GrammarNetEngine(string? apiKey, ILogger<GrammarNetEngine> logger)
    {
        _logger = logger;
        _apiKey = apiKey;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }
    }

    public async Task InitializeAsync()
    {
        // No initialization needed for HTTP-based service
        await Task.CompletedTask;
    }

    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey))
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        try
        {
            // This is a placeholder for GrammarNet API integration
            // In production, you would call the actual GrammarNet API

            var requestData = new
            {
                text = text,
                language = languageCode,
                enabledCategories = new[] { "grammar", "style", "spelling" }
            };

            var jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "https://api.grammar.net/v2/check",
                content,
                ct);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<GrammarNetResponse>(responseJson);

                if (result?.Matches != null)
                {
                    foreach (var match in result.Matches)
                    {
                        issues.Add(new GrammarIssue(
                            StartOffset: match.Offset,
                            EndOffset: match.Offset + match.Length,
                            Message: match.Message,
                            ShortMessage: match.ShortMessage ?? match.Rule.Category,
                            Replacements: match.Replacements?.Select(r => r.Value).ToList() ?? new List<string>(),
                            RuleId: $"GRAMMARNET_{match.Rule.Id}",
                            RuleDescription: match.Rule.Description,
                            Tags: new List<string> { match.Rule.Category.ToLowerInvariant() },
                            Context: text,
                            ContextOffset: Math.Max(0, match.Offset - 20),
                            ConfidenceLevel: match.Confidence
                        ));
                    }
                }
            }
            else
            {
                _logger.LogWarning("GrammarNet API returned {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GrammarNet API");
        }

        sw.Stop();
        return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    }

    public bool IsSupported(string languageCode)
    {
        // GrammarNet supports multiple languages
        return languageCode switch
        {
            "en-US" or "en-GB" or "en-CA" or "en-AU" => true,
            "de-DE" or "de-AT" or "de-CH" => true,
            "fr-FR" or "fr-CA" => true,
            "es-ES" or "es-MX" => true,
            _ => false
        };
    }

    private class GrammarNetResponse
    {
        public List<GrammarNetMatch>? Matches { get; set; }
    }

    private class GrammarNetMatch
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public string Message { get; set; } = null!;
        public string? ShortMessage { get; set; }
        public List<GrammarNetReplacement>? Replacements { get; set; }
        public GrammarNetRule Rule { get; set; } = null!;
        public int Confidence { get; set; }
    }

    private class GrammarNetReplacement
    {
        public string Value { get; set; } = null!;
    }

    private class GrammarNetRule
    {
        public string Id { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string Category { get; set; } = null!;
    }
}