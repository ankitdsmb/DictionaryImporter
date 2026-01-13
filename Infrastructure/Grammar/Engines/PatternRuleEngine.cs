// File: DictionaryImporter.Infrastructure/Grammar/Engines/PatternRuleEngine.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using System.Text.Json;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class PatternRuleEngine : IGrammarEngine, ITrainableGrammarEngine
{
    private readonly List<GrammarPatternRule> _rules = new();
    private readonly ILogger<PatternRuleEngine> _logger;
    private readonly string _rulesPath;

    public string Name => "PatternRules";
    public double ConfidenceWeight => 0.90;
    public bool CanTrain => true;

    public PatternRuleEngine(string rulesPath, ILogger<PatternRuleEngine> logger)
    {
        _logger = logger;
        _rulesPath = rulesPath;
        LoadRules();
    }

    private void LoadRules()
    {
        if (File.Exists(_rulesPath))
        {
            try
            {
                var json = File.ReadAllText(_rulesPath);
                var loadedRules = JsonSerializer.Deserialize<List<GrammarPatternRule>>(json);
                if (loadedRules != null)
                {
                    _rules.AddRange(loadedRules);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load pattern rules from {Path}", _rulesPath);
            }
        }

        AddBuiltInRules();
    }

    private void AddBuiltInRules()
    {
        // Add some basic built-in rules
        _rules.Add(new GrammarPatternRule
        {
            Pattern = @"\b(a|an)\s+[aeiou]\w+\b",
            Replacement = @"$1n",
            Description = "Indefinite article correction",
            Category = "GRAMMAR",
            Confidence = 95,
            Languages = new List<string> { "en" }
        });

        _rules.Add(new GrammarPatternRule
        {
            Pattern = @"\b(can not)\b",
            Replacement = "cannot",
            Description = "Standard contraction",
            Category = "SPELLING",
            Confidence = 100,
            Languages = new List<string> { "en" }
        });
    }

    public bool IsSupported(string languageCode) => true;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<GrammarCheckResult> CheckAsync(string text, string languageCode, CancellationToken ct)
    {
        var issues = new List<GrammarIssue>();

        foreach (var rule in _rules.Where(r => r.IsApplicable(languageCode)))
        {
            var matches = Regex.Matches(text, rule.Pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var corrected = Regex.Replace(match.Value, rule.Pattern, rule.Replacement);

                issues.Add(new GrammarIssue(
                    $"PATTERN_{rule.Id}",
                    rule.Description,
                    rule.Category,
                    match.Index,
                    match.Index + match.Length,
                    new List<string> { corrected },
                    rule.Confidence
                ));
            }
        }

        return Task.FromResult(new GrammarCheckResult(
            issues.Any(),
            issues.Count,
            issues,
            TimeSpan.Zero
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

    public async Task<bool> TrainAsync(GrammarFeedback feedback, CancellationToken ct)
    {
        if (feedback.OriginalIssue?.RuleId?.StartsWith("PATTERN_") != true)
            return false;

        var ruleId = feedback.OriginalIssue.RuleId.Replace("PATTERN_", "");
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);

        if (rule == null) return false;

        rule.UsageCount++;

        if (feedback.IsFalsePositive)
        {
            rule.Confidence = Math.Max(0, rule.Confidence - 10);
        }
        else if (feedback.IsValidCorrection)
        {
            rule.SuccessCount++;
            rule.Confidence = Math.Min(100, rule.Confidence + 5);
        }

        await SaveRulesAsync();
        return true;
    }

    private async Task SaveRulesAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_rules, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_rulesPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save pattern rules to {Path}", _rulesPath);
        }
    }
}