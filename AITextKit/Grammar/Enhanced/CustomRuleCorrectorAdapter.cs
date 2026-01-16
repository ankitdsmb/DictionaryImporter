using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.AITextKit.Grammar.Enhanced;

public sealed class CustomRuleCorrectorAdapter(
    CustomRuleEngine engine,
    ILogger<CustomRuleCorrectorAdapter> logger)
    : IGrammarCorrector
{
    private const RegexOptions DefaultOptions =
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant;

    public Task<GrammarCheckResult> CheckAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
        => Task.FromResult(new GrammarCheckResult(false, 0, [], TimeSpan.Zero));

    public Task<GrammarCorrectionResult> AutoCorrectAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: text,
                AppliedCorrections: [],
                RemainingIssues: []));
        }

        try
        {
            var rawRules = engine.GetRules(); // ✅ IReadOnlyList<GrammarPatternRule>

            if (rawRules.Count == 0)
            {
                return Task.FromResult(new GrammarCorrectionResult(
                    OriginalText: text,
                    CorrectedText: text,
                    AppliedCorrections: [],
                    RemainingIssues: []));
            }

            languageCode = NormalizeLanguage(languageCode);

            // ✅ Dedup + sort rules (important for dictionary)
            var rules = NormalizeAndDeduplicateRules(rawRules);

            var current = text;
            var applied = new List<AppliedCorrection>();

            foreach (var rule in rules)
            {
                ct.ThrowIfCancellationRequested();

                if (!IsValid(rule))
                    continue;

                // ✅ replacement can be empty, but never null
                var replacement = rule.Replacement ?? string.Empty;

                // ✅ Apply only if language matches
                if (!IsLanguageApplicable(rule, languageCode))
                    continue;

                Regex regex;
                try
                {
                    regex = new Regex(rule.Pattern, DefaultOptions);
                }
                catch (Exception rxEx)
                {
                    logger.LogWarning(rxEx,
                        "Invalid regex pattern in rule {RuleId}: {Pattern}",
                        rule.Id,
                        rule.Pattern);
                    continue;
                }

                if (!regex.IsMatch(current))
                    continue;

                var before = current;
                var after = regex.Replace(before, replacement);

                if (string.Equals(after, before, StringComparison.Ordinal))
                    continue;

                current = after;

                applied.Add(new AppliedCorrection(
                    $"REGEX_{rule.Id}",
                    rule.Id,
                    rule.Description ?? $"Regex replace: {rule.Pattern}",
                    current,
                    ClampConfidence(rule.Confidence)));

                // ✅ correct counters
                rule.UsageCount++;
                rule.SuccessCount++;
            }

            return Task.FromResult(new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: current,
                AppliedCorrections: applied,
                RemainingIssues: []));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "CustomRuleCorrectorAdapter failed");
            return Task.FromResult(new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: text,
                AppliedCorrections: [],
                RemainingIssues: []));
        }
    }

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GrammarSuggestion>>([]);

    private static bool IsValid(GrammarPatternRule rule)
    {
        if (rule is null) return false;
        if (string.IsNullOrWhiteSpace(rule.Id)) return false;
        if (string.IsNullOrWhiteSpace(rule.Pattern)) return false;
        return true;
    }

    private static string NormalizeLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return "all";

        return languageCode.Trim();
    }

    private static bool IsLanguageApplicable(GrammarPatternRule rule, string languageCode)
    {
        // if no languages provided => allow
        if (rule.Languages is null || rule.Languages.Count == 0)
            return true;

        // if rule includes "all" => allow always
        if (rule.Languages.Any(x => string.Equals(x, "all", StringComparison.OrdinalIgnoreCase)))
            return true;

        // if caller didn't provide language => allow dictionary cleanup
        if (string.Equals(languageCode, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        // exact match
        if (rule.Languages.Any(x => string.Equals(x, languageCode, StringComparison.OrdinalIgnoreCase)))
            return true;

        // prefix match: en matches en-US, en-GB
        if (rule.Languages.Any(x => languageCode.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static List<GrammarPatternRule> NormalizeAndDeduplicateRules(
        IReadOnlyList<GrammarPatternRule> rawRules)
    {
        // Key = Pattern + Replacement + Languages
        var map = new Dictionary<string, GrammarPatternRule>(StringComparer.Ordinal);

        foreach (var r in rawRules)
        {
            if (r is null) continue;
            if (string.IsNullOrWhiteSpace(r.Id)) continue;
            if (string.IsNullOrWhiteSpace(r.Pattern)) continue;

            var replacement = r.Replacement ?? string.Empty;

            var langs = (r.Languages is null || r.Languages.Count == 0)
                ? "all"
                : string.Join(",",
                    r.Languages
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim().ToLowerInvariant())
                        .OrderBy(x => x, StringComparer.Ordinal));

            var key = $"{r.Pattern}||{replacement}||{langs}";

            // Keep highest confidence if duplicate
            if (map.TryGetValue(key, out var existing))
            {
                if (r.Confidence > existing.Confidence)
                    map[key] = r;

                continue;
            }

            map[key] = r;
        }

        return map.Values
            .OrderBy(r => CategoryPriority(r.Category))
            .ThenByDescending(r => r.Confidence)
            .ToList();
    }

    private static int CategoryPriority(string? category)
    {
        category = category?.Trim()?.ToUpperInvariant() ?? "";

        return category switch
        {
            "DICTIONARY_FORMAT" => 1,
            "FORMATTING" => 2,
            "WHITESPACE" => 3,
            "PUNCTUATION" => 4,
            "CAPITALIZATION" => 5,
            "SPELLING" => 6,
            "GRAMMAR" => 7,
            _ => 99
        };
    }

    private static int ClampConfidence(int confidence)
    {
        if (confidence <= 0) return 90;
        if (confidence > 100) return 100;
        return confidence;
    }
}