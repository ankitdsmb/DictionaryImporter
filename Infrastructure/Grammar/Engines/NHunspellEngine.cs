// File: DictionaryImporter.Infrastructure/Grammar/Engines/NHunspellEngine.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using NHunspell;
using System.Diagnostics;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class NHunspellEngine(string dictionaryPath, ILogger<NHunspellEngine> logger) : IGrammarEngine
{
    private Hunspell? _hunspell;

    public string Name => "NHunspell";
    public double ConfidenceWeight => 0.95;

    public bool IsSupported(string languageCode)
    {
        var dictFile = Path.Combine(dictionaryPath, $"{languageCode}.dic");
        return File.Exists(dictFile);
    }

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                // Try to load en_US by default
                var affPath = Path.Combine(dictionaryPath, "en_US.aff");
                var dicPath = Path.Combine(dictionaryPath, "en_US.dic");

                if (File.Exists(affPath) && File.Exists(dicPath))
                {
                    _hunspell = new Hunspell(affPath, dicPath);
                    logger?.LogInformation("NHunspell initialized successfully");
                }
                else
                {
                    logger?.LogWarning("NHunspell dictionary files not found at {Path}", dictionaryPath);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to initialize NHunspell");
            }
        });
    }

    public Task<GrammarCheckResult> CheckAsync(string text, string languageCode, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        if (_hunspell == null || string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new GrammarCheckResult(false, 0, issues, sw.Elapsed));
        }

        // Simple word-by-word checking
        var words = Regex.Matches(text, @"\b\w+\b");

        foreach (Match word in words)
        {
            ct.ThrowIfCancellationRequested();

            if (!_hunspell.Spell(word.Value))
            {
                var suggestions = _hunspell.Suggest(word.Value);
                issues.Add(new GrammarIssue(
                    $"SPELLING_{word.Value}",
                    $"Misspelled word: {word.Value}",
                    "SPELLING",
                    word.Index,
                    word.Index + word.Length,
                    suggestions.Take(5).ToList(),
                    95
                ));
            }
        }

        sw.Stop();
        return Task.FromResult(new GrammarCheckResult(
            issues.Any(),
            issues.Count,
            issues,
            sw.Elapsed
        ));
    }

    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode, CancellationToken ct)
    {
        var checkResult = CheckAsync(text, languageCode, ct).GetAwaiter().GetResult();
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

        return Task.FromResult(new GrammarCorrectionResult(
            text,
            correctedText,
            appliedCorrections,
            checkResult.Issues
        ));
    }
}