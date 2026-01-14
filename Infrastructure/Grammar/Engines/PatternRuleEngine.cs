using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class PatternRuleEngine(string rulesFilePath, ILogger<PatternRuleEngine> logger) : IGrammarEngine
{
    private List<GrammarPatternRule> _rules = [];
    private bool _initialized = false;
    private readonly object _lock = new();

    public string Name => "PatternRules";
    public double ConfidenceWeight => 0.90;

    public bool IsSupported(string languageCode)
    {
        return _rules.Any(r => r.IsApplicable(languageCode)) ||
               _rules.Any(r => r.Languages.Contains("all", StringComparer.OrdinalIgnoreCase));
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await Task.Run(() =>
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(rulesFilePath))
                    {
                        var json = File.ReadAllText(rulesFilePath);
                        var loadedRules = JsonSerializer.Deserialize<List<GrammarPatternRule>>(json);
                        if (loadedRules != null)
                        {
                            _rules = loadedRules;
                            logger.LogInformation("Loaded {Count} pattern rules from {Path}",
                                _rules.Count, rulesFilePath);
                        }
                    }

                    if (_rules.Count == 0)
                    {
                        AddDefaultRules();
                        logger.LogInformation("Added {Count} default pattern rules", _rules.Count);
                    }

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to initialize PatternRuleEngine");
                    AddDefaultRules();
                    _initialized = true;
                }
            }
        });
    }

    public async Task<GrammarCheckResult> CheckAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }

        if (string.IsNullOrWhiteSpace(text) || !IsSupported(languageCode))
        {
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        try
        {
            var applicableRules = _rules
                .Where(r => r.IsApplicable(languageCode))
                .OrderByDescending(r => r.Confidence)
                .ToList();

            foreach (var rule in applicableRules)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
                    var matches = regex.Matches(text);

                    foreach (Match match in matches)
                    {
                        if (!match.Success) continue;
                        var startOffset = match.Index;
                        var endOffset = match.Index + match.Length;
                        var contextStart = Math.Max(0, startOffset - 20);
                        var contextLength = Math.Min(40, text.Length - contextStart);
                        var context = text.Substring(contextStart, contextLength);

                        var replacement = regex.Replace(match.Value, rule.Replacement);

                        var issue = new GrammarIssue(
                            StartOffset: startOffset,
                            EndOffset: endOffset,
                            Message: rule.Description,
                            ShortMessage: rule.Category,
                            Replacements: new List<string> { replacement },
                            RuleId: $"PATTERN_{rule.Id}",
                            RuleDescription: rule.Description,
                            Tags: new List<string> { rule.Category.ToLowerInvariant() },
                            Context: context,
                            ContextOffset: Math.Max(0, startOffset - 20),
                            ConfidenceLevel: rule.Confidence
                        );

                        issues.Add(issue);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error applying pattern rule {RuleId}: {Pattern}",
                        rule.Id, rule.Pattern);
                }
            }

            sw.Stop();
            return new GrammarCheckResult(true, issues.Count, issues, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in PatternRuleEngine check");
            sw.Stop();
            return new GrammarCheckResult(false, 0, [], sw.Elapsed);
        }
    }

    private void AddDefaultRules()
    {
        _rules.AddRange([
            new GrammarPatternRule(id: "DOUBLE_SPACE", pattern: @"\s{2,}", replacement: " ",
                description: "Replace multiple spaces with single space", category: "WHITESPACE", confidence: 95,
                languages: ["en", "all"]),
            new GrammarPatternRule(id: "MISSING_APOSTROPHE_ITS", pattern: @"\bits\s", replacement: "it's ",
                description: "Correct 'its' to 'it's' when used as contraction", category: "GRAMMAR", confidence: 80,
                languages: ["en"]),
            new GrammarPatternRule(id: "REPEATED_WORD", pattern: @"\b(\w+)\s+\1\b", replacement: "$1",
                description: "Remove repeated consecutive words", category: "STYLE", confidence: 90,
                languages: ["en", "all"]),
            new GrammarPatternRule(id: "ALOT_TO_A_LOT", pattern: @"\balot\b", replacement: "a lot",
                description: "Correct 'alot' to 'a lot'", category: "SPELLING", confidence: 99, languages: ["en"],
                usageCount: 0, successCount: 0),
            new GrammarPatternRule(id: "I_AM_CONTRACTION", pattern: @"\bi\s+am\b", replacement: "I'm",
                description: "Convert 'i am' to 'I'm'", category: "CONTRACTION", confidence: 85, languages: ["en"],
                usageCount: 0, successCount: 0),
            new GrammarPatternRule(id: "ITS_IT_S_CONFUSION", pattern: @"\b(it's)\b", replacement: "its",
                description: "Correct 'it's' (possessive) to 'its'", category: "GRAMMAR", confidence: 80,
                languages: ["en"], usageCount: 0, successCount: 0)
        ]);

        logger.LogInformation("Added {Count} default pattern rules", _rules.Count);
    }
}