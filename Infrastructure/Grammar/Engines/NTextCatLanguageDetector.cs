// File: DictionaryImporter/Infrastructure/Grammar/Engines/SimpleLanguageDetector.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using System.Diagnostics;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class SimpleLanguageDetector : IGrammarEngine
{
    private readonly ILogger<SimpleLanguageDetector> _logger;
    private bool _initialized = false;

    public string Name => "SimpleLanguageDetector";
    public double ConfidenceWeight => 0.70;

    public SimpleLanguageDetector(ILogger<SimpleLanguageDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task InitializeAsync()
    {
        _initialized = true;
        _logger.LogInformation("SimpleLanguageDetector initialized");
        return Task.CompletedTask;
    }

    //public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    //{
    //    if (!_initialized || string.IsNullOrWhiteSpace(text) || text.Length < 10)
    //        return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

    //    var sw = Stopwatch.StartNew();
    //    var issues = new List<GrammarIssue>();

    //    await Task.Run(() =>
    //    {
    //        try
    //        {
    //            // Simple English detection heuristic
    //            var englishScore = CalculateEnglishScore(text);
    //            var expectedIsEnglish = languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    //            if (expectedIsEnglish && englishScore < 0.3)
    //            {
    //                issues.Add(new GrammarIssue(
    //                    StartOffset: 0,
    //                    EndOffset: Math.Min(50, text.Length),
    //                    Message: $"Text appears to contain significant non-English content",
    //                    ShortMessage: "Language detection",
    //                    Replacements: new List<string>(),
    //                    RuleId: "LANGUAGE_DETECTION",
    //                    RuleDescription: "Language content detection",
    //                    Tags: new List<string> { "language", "detection" },
    //                    Context: text.Substring(0, Math.Min(100, text.Length)),
    //                    ContextOffset: 0,
    //                    ConfidenceLevel: 70
    //                ));
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Error during language detection");
    //        }
    //    }, ct);

    //    sw.Stop();
    //    return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    //}
    // File: DictionaryImporter/Infrastructure/Grammar/Engines/SimpleLanguageDetector.cs (updated section)
    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(text) || text.Length < 10)
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        await Task.Run(() =>
        {
            try
            {
                // Simple English detection heuristic
                var englishScore = CalculateEnglishScore(text);
                var expectedIsEnglish = languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase);

                if (expectedIsEnglish && englishScore < 0.3)
                {
                    issues.Add(new GrammarIssue(
                        StartOffset: 0,
                        EndOffset: Math.Min(50, text.Length),
                        Message: $"Text appears to contain significant non-English content",
                        ShortMessage: "Language detection",
                        Replacements: new List<string>(),
                        RuleId: "LANGUAGE_DETECTION",
                        RuleDescription: "Language content detection",
                        Tags: new List<string> { "language", "detection" },  // Required Tags parameter
                        Context: text.Substring(0, Math.Min(100, text.Length)),
                        ContextOffset: 0,
                        ConfidenceLevel: 70
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during language detection");
            }
        }, ct);

        sw.Stop();
        return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    }

    private double CalculateEnglishScore(string text)
    {
        // Count common English words
        var commonEnglishWords = new HashSet<string>
        {
            "the", "be", "to", "of", "and", "a", "in", "that", "have", "I",
            "it", "for", "not", "on", "with", "he", "as", "you", "do", "at"
        };

        var words = Regex.Matches(text.ToLower(), @"\b[a-z]+\b")
            .Cast<Match>()
            .Select(m => m.Value)
            .ToList();

        if (words.Count == 0) return 0;

        var englishWordCount = words.Count(w => commonEnglishWords.Contains(w));
        return (double)englishWordCount / words.Count;
    }

    public bool IsSupported(string languageCode)
    {
        // Simple detector works for all languages but only checks for English
        return true;
    }
}