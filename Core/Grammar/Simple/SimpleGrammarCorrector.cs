// File: DictionaryImporter.Core/Grammar/SimpleGrammarCorrector.cs
using System.Net.Http.Json;
using System.Text.Json;
using DictionaryImporter.Core.Grammar.Enhanced;

namespace DictionaryImporter.Core.Grammar.Simple;

/// <summary>
/// Simple grammar corrector using LanguageTool
/// </summary>
public sealed class SimpleGrammarCorrector(
    string baseUrl = "http://localhost:2026",
    ILogger<SimpleGrammarCorrector>? logger = null)
    : IGrammarCorrector
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        BaseAddress = new Uri(baseUrl)
    };

    private readonly string _baseUrl = baseUrl;

    public async Task<GrammarCheckResult> CheckAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["language"] = languageCode,
                ["enabledOnly"] = "false"
            });

            var response = await _httpClient.PostAsync("/v2/check", form, ct);
            response.EnsureSuccessStatusCode();

            var ltResponse =
                await response.Content.ReadFromJsonAsync<LanguageToolResponse>(cancellationToken: ct)
                ?? new LanguageToolResponse();

            var issues = ConvertToIssues(ltResponse);

            sw.Stop();

            return new GrammarCheckResult(
                issues.Any(),
                issues.Count,
                issues,
                sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "LanguageTool check failed for text: {Text}",
                text.Length > 50 ? text[..50] + "..." : text);

            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);
        }
    }

    public async Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCorrectionResult(text, text, Array.Empty<AppliedCorrection>(), Array.Empty<GrammarIssue>());

        var checkResult = await CheckAsync(text, languageCode, ct);

        if (!checkResult.HasIssues)
            return new GrammarCorrectionResult(text, text, Array.Empty<AppliedCorrection>(), Array.Empty<GrammarIssue>());

        var correctedText = text;
        var appliedCorrections = new List<AppliedCorrection>();

        // Apply corrections from end to start to maintain positions
        foreach (var issue in checkResult.Issues.OrderByDescending(i => i.StartOffset))
        {
            if (issue.Replacements.Count == 0) continue;

            // Use the first replacement
            var replacement = issue.Replacements[0];
            var originalSegment = correctedText.Substring(
                issue.StartOffset,
                issue.EndOffset - issue.StartOffset);

            correctedText = correctedText.Remove(
                issue.StartOffset,
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

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        // Simple suggestions based on text analysis
        var suggestions = new List<GrammarSuggestion>();

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<IReadOnlyList<GrammarSuggestion>>(suggestions);

        // Suggest breaking long sentences
        if (text.Length > 100 && text.Count(c => c == '.') < 2)
        {
            suggestions.Add(new GrammarSuggestion(
                text,
                "Consider breaking this into shorter sentences.",
                "Long sentences can reduce readability.",
                "clarity"
            ));
        }

        // Suggest active voice
        if (text.Contains(" is ") || text.Contains(" was ") || text.Contains(" were "))
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.EndsWith("ed") && w.Length > 3))
            {
                suggestions.Add(new GrammarSuggestion(
                    text,
                    "Consider using active voice for more direct statements.",
                    "Passive voice can reduce impact.",
                    "style"
                ));
            }
        }

        return Task.FromResult<IReadOnlyList<GrammarSuggestion>>(suggestions);
    }

    private List<GrammarIssue> ConvertToIssues(LanguageToolResponse response)
    {
        var issues = new List<GrammarIssue>();

        if (response?.Matches == null)
            return issues;

        foreach (var match in response.Matches)
        {
            issues.Add(new GrammarIssue(
                match.Rule.Id,
                match.Message,
                match.Rule.Category.Name,
                match.Offset,
                match.Offset + match.Length,
                match.Replacements?.Select(r => r.Value).ToList() ?? new List<string>(),
                CalculateConfidence(match)
            ));
        }

        return issues;
    }

    private int CalculateConfidence(LanguageToolMatch match)
    {
        var baseConfidence = 70; // Default

        if (match.Rule.Category.Id == "TYPOS")
            baseConfidence = 95;
        else if (match.Rule.Category.Id == "GRAMMAR")
            baseConfidence = 80;
        else if (match.Rule.Category.Id == "STYLE")
            baseConfidence = 60;

        // Higher confidence if replacements available
        if (match.Replacements?.Count > 0)
            baseConfidence = Math.Min(100, baseConfidence + 10);

        return baseConfidence;
    }
}