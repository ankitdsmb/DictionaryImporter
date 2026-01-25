using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;
using DictionaryImporter.Gateway.Grammar.Engines;

namespace DictionaryImporter.Gateway.Grammar.Correctors;

public sealed class CustomRuleCorrectorAdapter(
    ICustomGrammarRuleEngine engineWrapper,
    ILogger<CustomRuleCorrectorAdapter> logger)
    : IGrammarCorrector
{
    private const RegexOptions DefaultOptions =
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant;

    private const int MaxAppliedCorrections = 200;

    // ✅ Cache compiled regex by pattern string
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache =
        new(StringComparer.Ordinal);

    private readonly CustomRuleEngine engine = engineWrapper.Engine;

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
            var rawRules = engine.GetRules();

            if (rawRules.Count == 0)
            {
                return Task.FromResult(new GrammarCorrectionResult(
                    OriginalText: text,
                    CorrectedText: text,
                    AppliedCorrections: [],
                    RemainingIssues: []));
            }

            languageCode = NormalizeLanguage(languageCode);

            var rules = NormalizeAndDeduplicateRules(rawRules);

            var current = text;
            var applied = new List<AppliedCorrection>();

            foreach (var rule in rules)
            {
                ct.ThrowIfCancellationRequested();

                if (applied.Count >= MaxAppliedCorrections)
                    break;

                if (!IsValid(rule))
                    continue;

                if (!rule.Enabled)
                    continue;

                if (!IsLanguageApplicable(rule, languageCode))
                    continue;

                var regex = GetOrCreateRegex(rule, logger);
                if (regex is null)
                    continue;

                if (!regex.IsMatch(current))
                    continue;

                var before = current;
                var replacement = rule.Replacement ?? string.Empty;
                var after = regex.Replace(before, replacement);

                if (string.IsNullOrWhiteSpace(after))
                    continue;

                if (string.Equals(after, before, StringComparison.Ordinal))
                    continue;

                current = after;

                applied.Add(new AppliedCorrection(
                    $"REGEX_{rule.Id}",
                    rule.Id,
                    string.IsNullOrWhiteSpace(rule.Description)
                        ? $"Regex replace: {rule.Pattern}"
                        : rule.Description,
                    after,
                    ClampConfidence(rule.Confidence)));
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

    private static Regex? GetOrCreateRegex(
        GrammarPatternRule rule,
        ILogger<CustomRuleCorrectorAdapter> logger)
    {
        var cacheKey = $"{rule.Pattern}||{(int)DefaultOptions}";

        return RegexCache.GetOrAdd(cacheKey, _ =>
        {
            try
            {
                return new Regex(rule.Pattern, DefaultOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Invalid regex pattern in rule {RuleId}: {Pattern}",
                    rule.Id,
                    rule.Pattern);

                return null;
            }
        });
    }

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
        if (rule.Languages is null || rule.Languages.Count == 0)
            return true;

        if (rule.Languages.Any(x => string.Equals(x, "all", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (string.Equals(languageCode, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        if (rule.Languages.Any(x => string.Equals(x, languageCode, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (rule.Languages.Any(x => languageCode.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static List<GrammarPatternRule> NormalizeAndDeduplicateRules(
        IReadOnlyList<GrammarPatternRule> rawRules)
    {
        var map = new Dictionary<string, GrammarPatternRule>(StringComparer.Ordinal);

        foreach (var r in rawRules)
        {
            if (r is null) continue;
            if (string.IsNullOrWhiteSpace(r.Id)) continue;
            if (string.IsNullOrWhiteSpace(r.Pattern)) continue;

            var replacement = r.Replacement ?? string.Empty;

            var langs = r.Languages is null || r.Languages.Count == 0
                ? "all"
                : string.Join(",",
                    r.Languages
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim().ToLowerInvariant())
                        .OrderBy(x => x, StringComparer.Ordinal));

            var key = $"{r.Pattern}||{replacement}||{langs}";

            if (map.TryGetValue(key, out var existing))
            {
                // Prefer enabled rule, then higher confidence, then lower priority
                if (existing.Enabled != r.Enabled)
                {
                    if (r.Enabled)
                        map[key] = r;
                    continue;
                }

                if (r.Confidence > existing.Confidence)
                    map[key] = r;

                continue;
            }

            map[key] = r;
        }

        return map.Values
            .OrderBy(r => CategoryPriority(r.Category))
            .ThenBy(r => r.Priority)
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
            "STYLE" => 8,
            "CLEANUP" => 9,
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