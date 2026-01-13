// File: DictionaryImporter/Infrastructure/Grammar/Engines/PatternRuleEngine.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using System.Diagnostics;
using System.Text.Json;
using DictionaryImporter.Infrastructure.Grammar.Helper;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class PatternRuleEngine : IGrammarEngine, DictionaryImporter.Core.Grammar.Enhanced.ITrainableGrammarEngine
{
    private readonly string _rulesFilePath;
    private readonly ILogger<PatternRuleEngine> _logger;
    private readonly object _lock = new();
    private List<GrammarPatternRule> _rules = new();
    private readonly Dictionary<string, Regex> _compiledPatterns = new();
    private bool _initialized = false;

    public string Name => "PatternRules";
    public double ConfidenceWeight => 0.90;
    public bool CanTrain => true;

    public PatternRuleEngine(string rulesFilePath, ILogger<PatternRuleEngine> logger)
    {
        _rulesFilePath = rulesFilePath ?? throw new ArgumentNullException(nameof(rulesFilePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            await LoadRulesAsync();
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize PatternRuleEngine");
            CreateDefaultRules();
            _initialized = true;
        }
    }

    private async Task LoadRulesAsync()
    {
        if (!File.Exists(_rulesFilePath))
        {
            _logger.LogInformation("Pattern rules file not found at {Path}, creating default rules", _rulesFilePath);
            CreateDefaultRules();
            await SaveRulesAsync();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_rulesFilePath);
            var loadedRules = JsonSerializer.Deserialize<List<GrammarPatternRule>>(json);

            lock (_lock)
            {
                _rules = loadedRules ?? new List<GrammarPatternRule>();
                CompilePatterns();
                _logger.LogInformation("Loaded {Count} pattern rules from {Path}", _rules.Count, _rulesFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pattern rules from {Path}", _rulesFilePath);
            CreateDefaultRules();
        }
    }

    private void CompilePatterns()
    {
        _compiledPatterns.Clear();
        foreach (var rule in _rules)
        {
            try
            {
                var regex = new Regex(rule.Pattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
                _compiledPatterns[rule.Id] = regex;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern in rule {RuleId}: {Pattern}", rule.Id, rule.Pattern);
            }
        }
    }

    private void CreateDefaultRules()
    {
        lock (_lock)
        {
            _rules = new List<GrammarPatternRule>
            {
                new()
                {
                    Id = "ALOT_TO_A_LOT",
                    Pattern = @"\balot\b",
                    Replacement = "a lot",
                    Description = "Correct 'alot' to 'a lot'",
                    Category = "SPELLING",
                    Confidence = 99,
                    Languages = new List<string> { "en" },
                    UsageCount = 0,
                    SuccessCount = 0
                },
                new()
                {
                    Id = "I_AM_CONTRACTION",
                    Pattern = @"\bi\s+am\b",
                    Replacement = "I'm",
                    Description = "Convert 'i am' to 'I'm'",
                    Category = "CONTRACTION",
                    Confidence = 85,
                    Languages = new List<string> { "en" },
                    UsageCount = 0,
                    SuccessCount = 0
                },
                new()
                {
                    Id = "ITS_IT_S_CONFUSION",
                    Pattern = @"\b(it's)\b",
                    Replacement = "its",
                    Description = "Correct 'it's' (possessive) to 'its'",
                    Category = "GRAMMAR",
                    Confidence = 80,
                    Languages = new List<string> { "en" },
                    UsageCount = 0,
                    SuccessCount = 0
                }
            };

            CompilePatterns();
            _logger.LogInformation("Created {Count} default pattern rules", _rules.Count);
        }
    }

    //public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    //{
    //    if (!_initialized || string.IsNullOrWhiteSpace(text))
    //        return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

    //    var sw = Stopwatch.StartNew();
    //    var issues = new List<GrammarIssue>();

    //    await Task.Run(() =>
    //    {
    //        try
    //        {
    //            List<GrammarPatternRule> applicableRules;
    //            Dictionary<string, Regex> patterns;

    //            lock (_lock)
    //            {
    //                applicableRules = _rules
    //                    .Where(r => r.IsApplicable(languageCode))
    //                    .ToList();
    //                patterns = new Dictionary<string, Regex>(_compiledPatterns);
    //            }

    //            foreach (var rule in applicableRules)
    //            {
    //                ct.ThrowIfCancellationRequested();

    //                if (!patterns.TryGetValue(rule.Id, out var regex))
    //                    continue;

    //                var matches = regex.Matches(text);
    //                foreach (Match match in matches)
    //                {
    //                    if (!match.Success) continue;

    //                    // Calculate replacement
    //                    string replacement;
    //                    try
    //                    {
    //                        replacement = regex.Replace(match.Value, rule.Replacement);
    //                    }
    //                    catch
    //                    {
    //                        replacement = rule.Replacement;
    //                    }

    //                    // Skip if replacement is same as original (case-insensitive)
    //                    if (string.Equals(match.Value, replacement, StringComparison.OrdinalIgnoreCase))
    //                        continue;

    //                    issues.Add(new GrammarIssue(
    //                        StartOffset: match.Index,
    //                        EndOffset: match.Index + match.Length,
    //                        Message: rule.Description,
    //                        ShortMessage: rule.Category,
    //                        Replacements: new List<string> { replacement },
    //                        RuleId: $"PATTERN_{rule.Id}",
    //                        RuleDescription: rule.Description,
    //                        Tags: new List<string> { rule.Category.ToLowerInvariant() },
    //                        Context: GetContext(text, match.Index),
    //                        ContextOffset: Math.Max(0, match.Index - 20),
    //                        ConfidenceLevel: rule.Confidence
    //                    ));
    //                }
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Error during pattern rule checking");
    //        }
    //    }, ct);

    //    sw.Stop();
    //    return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    //}
    // File: DictionaryImporter/Infrastructure/Grammar/Engines/PatternRuleEngine.cs (updated section)
    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        await Task.Run(() =>
        {
            try
            {
                List<GrammarPatternRule> applicableRules;
                Dictionary<string, Regex> patterns;

                lock (_lock)
                {
                    applicableRules = _rules
                        .Where(r => r.IsApplicable(languageCode))
                        .ToList();
                    patterns = new Dictionary<string, Regex>(_compiledPatterns);
                }

                foreach (var rule in applicableRules)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!patterns.TryGetValue(rule.Id, out var regex))
                        continue;

                    var matches = regex.Matches(text);
                    foreach (Match match in matches)
                    {
                        if (!match.Success) continue;

                        // Calculate replacement
                        string replacement;
                        try
                        {
                            replacement = regex.Replace(match.Value, rule.Replacement);
                        }
                        catch
                        {
                            replacement = rule.Replacement;
                        }

                        // Skip if replacement is same as original (case-insensitive)
                        if (string.Equals(match.Value, replacement, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // In PatternRuleEngine CheckAsync method, fix the GrammarIssue creation:
                        var issue = GrammarIssueHelper.CreatePatternRuleIssue(
                            match.Index,
                            match.Index + match.Length,
                            rule.Id,
                            rule.Description,
                            rule.Category,
                            replacement,
                            GetContext(text, match.Index),
                            rule.Confidence
                        );
                        issues.Add(issue);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pattern rule checking");
            }
        }, ct);

        sw.Stop();
        return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    }

    private string GetContext(string text, int position, int contextLength = 50)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var start = Math.Max(0, position - contextLength);
        var end = Math.Min(text.Length, position + contextLength);
        return text.Substring(start, end - start);
    }

    public async Task<bool> TrainAsync(GrammarFeedback feedback, CancellationToken ct = default)
    {
        if (feedback.OriginalIssue == null || !feedback.OriginalIssue.RuleId.StartsWith("PATTERN_"))
            return false;

        var ruleId = feedback.OriginalIssue.RuleId.Replace("PATTERN_", "");
        bool trained = false;

        lock (_lock)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.UsageCount++;
                trained = true;

                if (feedback.IsFalsePositive)
                {
                    // Decrease confidence for false positives
                    rule.Confidence = Math.Max(0, rule.Confidence - 10);
                    _logger.LogDebug("Decreased confidence for rule {RuleId} to {Confidence} due to false positive",
                        ruleId, rule.Confidence);
                }
                else if (feedback.IsValidCorrection)
                {
                    // Increase confidence for valid corrections
                    rule.SuccessCount++;
                    rule.Confidence = Math.Min(100, rule.Confidence + 5);
                    _logger.LogDebug("Increased confidence for rule {RuleId} to {Confidence} due to valid correction",
                        ruleId, rule.Confidence);
                }

                // Auto-adjust pattern if we have enough data
                if (rule.UsageCount > 10 && rule.SuccessCount / (double)rule.UsageCount < 0.3)
                {
                    _logger.LogWarning("Rule {RuleId} has low success rate ({Success}/{Total}), consider revising",
                        ruleId, rule.SuccessCount, rule.UsageCount);
                }
            }
        }

        if (trained)
        {
            // Save updated rules asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveRulesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save rules after training");
                }
            }, ct);
        }

        return trained;
    }

    private async Task SaveRulesAsync()
    {
        try
        {
            List<GrammarPatternRule> rulesCopy;
            lock (_lock)
            {
                rulesCopy = new List<GrammarPatternRule>(_rules);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(rulesCopy, options);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(_rulesFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(_rulesFilePath, json);
            _logger.LogDebug("Saved {Count} pattern rules to {Path}", rulesCopy.Count, _rulesFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save pattern rules to {Path}", _rulesFilePath);
        }
    }

    public void AddRule(GrammarPatternRule rule)
    {
        lock (_lock)
        {
            _rules.Add(rule);
            try
            {
                var regex = new Regex(rule.Pattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
                _compiledPatterns[rule.Id] = regex;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern in new rule {RuleId}", rule.Id);
            }
        }
    }

    public void RemoveRule(string ruleId)
    {
        lock (_lock)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
            _compiledPatterns.Remove(ruleId);
        }
    }

    public IReadOnlyList<GrammarPatternRule> GetRules()
    {
        lock (_lock)
        {
            return _rules.AsReadOnly();
        }
    }

    public bool IsSupported(string languageCode)
    {
        lock (_lock)
        {
            return _rules.Any(r => r.IsApplicable(languageCode));
        }
    }
}