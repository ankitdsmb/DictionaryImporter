using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;
using DictionaryImporter.Gateway.Grammar.Engines;
using DictionaryImporter.Gateway.Rewriter;

namespace DictionaryImporter.Gateway.Grammar.Correctors;

public sealed class DictionaryRewriteCorrectorAdapter(
    ICustomDictionaryRewriteRuleEngine engineWrapper,
    RewriteMapEngine rewriteMapEngine,
    DictionaryHumanizer dictionaryHumanizer,
    IRewriteContextAccessor rewriteContextAccessor,
    ILogger<DictionaryRewriteCorrectorAdapter> logger,
    IRewriteRuleHitRepository? hitRepository = null
) : IGrammarCorrector
{
    private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private const int MaxAppliedCorrections = 200;

    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);

    private readonly CustomRuleEngine _engine = engineWrapper.Engine;
    private readonly RewriteMapEngine _rewriteMapEngine = rewriteMapEngine;
    private readonly DictionaryHumanizer _dictionaryHumanizer = dictionaryHumanizer;
    private readonly IRewriteContextAccessor _rewriteContextAccessor = rewriteContextAccessor;
    private readonly ILogger<DictionaryRewriteCorrectorAdapter> _logger = logger;

    private readonly IRewriteRuleHitRepository? _hitRepository = hitRepository;

    public Task<GrammarCheckResult> CheckAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
        => Task.FromResult(new GrammarCheckResult(false, 0, [], TimeSpan.Zero));

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<GrammarSuggestion>>(Array.Empty<GrammarSuggestion>());
    }

    public async Task<GrammarCorrectionResult> AutoCorrectAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: text,
                AppliedCorrections: [],
                RemainingIssues: []);
        }

        try
        {
            var pass1 = await ApplyRewriteOnceAsync(text, ct);
            var pass2 = await ApplyRewriteOnceAsync(pass1.CorrectedText, ct);

            if (!string.Equals(pass2.CorrectedText, pass1.CorrectedText, StringComparison.Ordinal))
            {
                var applied = pass1.AppliedCorrections.ToList();

                applied.Add(new AppliedCorrection(
                    "IDEMPOTENCY_GUARD",
                    "IdempotencyGuard",
                    "Second-pass rewrite changed output again; keeping first-pass result to prevent oscillation.",
                    pass1.CorrectedText,
                    100));

                return new GrammarCorrectionResult(
                    OriginalText: pass1.OriginalText,
                    CorrectedText: pass1.CorrectedText,
                    AppliedCorrections: applied,
                    RemainingIssues: []);
            }

            return pass1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DictionaryRewriteCorrectorAdapter idempotency wrapper failed. Returning original.");

            return new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: text,
                AppliedCorrections: [],
                RemainingIssues: []);
        }
    }

    private async Task<GrammarCorrectionResult> ApplyRewriteOnceAsync(string text, CancellationToken ct)
    {
        var original = text;
        var current = text;

        Dictionary<string, long>? jsonRegexHitBuffer = null;

        ProtectedTokenGuard.ProtectedTokenResult protectedResult =
            new ProtectedTokenGuard.ProtectedTokenResult(current, new Dictionary<string, string>(0));

        try
        {
            current = current.Trim();
            var applied = new List<AppliedCorrection>();

            var ctx = _rewriteContextAccessor.Current;
            var sourceCode = NormalizeSource(ctx.SourceCode);
            var mode = ctx.Mode;

            // Protected Tokens Guard (before rewrites)
            protectedResult = ProtectedTokenGuard.Protect(current);
            current = protectedResult.ProtectedText;

            // Phase 1: JSON Regex Rules
            var rawRules = _engine.GetRules();
            if (rawRules.Count > 0)
            {
                var rules = rawRules
                    .Where(r => r.Enabled)
                    .OrderBy(r => r.Priority)
                    .ThenByDescending(r => r.Confidence)
                    .ToList();

                foreach (var rule in rules)
                {
                    ct.ThrowIfCancellationRequested();

                    if (applied.Count >= MaxAppliedCorrections)
                        break;

                    if (string.IsNullOrWhiteSpace(rule.Pattern))
                        continue;

                    var regex = GetOrCreateRegex(rule, _logger);
                    if (regex is null)
                        continue;

                    if (!regex.IsMatch(current))
                        continue;

                    var after = regex.Replace(current, rule.Replacement ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(after))
                        continue;

                    if (string.Equals(after, current, StringComparison.Ordinal))
                        continue;

                    current = after;

                    applied.Add(new AppliedCorrection(
                        $"REGEX_{rule.Id}",
                        rule.Id,
                        rule.Description ?? "Dictionary rewrite rule",
                        current,
                        ClampConfidence(rule.Confidence)));

                    jsonRegexHitBuffer ??= new Dictionary<string, long>(StringComparer.Ordinal);

                    var ruleKey = BuildJsonRegexRuleKey(rule);
                    if (jsonRegexHitBuffer.TryGetValue(ruleKey, out var existing))
                        jsonRegexHitBuffer[ruleKey] = existing + 1;
                    else
                        jsonRegexHitBuffer[ruleKey] = 1;
                }
            }

            await TryFlushJsonRegexHitsAsync(
                sourceCode: sourceCode,
                mode: mode,
                hitBuffer: jsonRegexHitBuffer,
                ct: ct);

            // Phase 2: SQL RewriteMap Engine
            var mapResult = await _rewriteMapEngine.ApplyAsync(
                current,
                sourceCode: sourceCode,
                mode: mode,
                ct: ct);

            if (!string.IsNullOrWhiteSpace(mapResult.RewrittenText) &&
                !string.Equals(mapResult.RewrittenText, current, StringComparison.Ordinal))
            {
                current = mapResult.RewrittenText;

                applied.Add(new AppliedCorrection(
                    "REWRITEMAP",
                    "RewriteMap",
                    $"SQL RewriteMap applied ({sourceCode}/{mode})",
                    current,
                    95));
            }

            // Phase 2.5: Abbreviation Standardizer (fallback, deterministic, meaning-safe)
            var abbr = DictionaryAbbreviationStandardizer.StandardizeSafe(current);
            if (abbr.Changed &&
                !string.IsNullOrWhiteSpace(abbr.Text) &&
                !string.Equals(abbr.Text, current, StringComparison.Ordinal))
            {
                current = abbr.Text;

                applied.Add(new AppliedCorrection(
                    "ABBREVIATION_STANDARDIZER",
                    "AbbreviationStandardizer",
                    $"Standardized dictionary abbreviations ({string.Join(",", abbr.AppliedKeys)})",
                    current,
                    99));
            }

            // Phase 3: Humanizer-safe formatting
            var humanized = HumanizeByMode(mode, current);
            if (!string.IsNullOrWhiteSpace(humanized) &&
                !string.Equals(humanized, current, StringComparison.Ordinal))
            {
                current = humanized;

                applied.Add(new AppliedCorrection(
                    "HUMANIZER",
                    "Humanizer",
                    "DictionaryHumanizer applied",
                    current,
                    98));
            }

            // Restore Protected Tokens
            if (protectedResult.HasTokens)
            {
                var restored = ProtectedTokenGuard.Restore(current, protectedResult.Map);
                if (!string.IsNullOrWhiteSpace(restored))
                    current = restored;
            }

            // Punctuation + Spacing Normalizer
            var normalizedPunct = PunctuationNormalizer.Normalize(current);
            if (!string.IsNullOrWhiteSpace(normalizedPunct) &&
                !string.Equals(normalizedPunct, current, StringComparison.Ordinal))
            {
                current = normalizedPunct;

                applied.Add(new AppliedCorrection(
                    "PUNCT_NORMALIZER",
                    "PunctuationNormalizer",
                    "Punctuation and spacing normalized (meaning-safe).",
                    current,
                    99));
            }

            // NEW: Brackets / Quotes Balancer (safe mode)
            var balance = BracketQuoteBalancer.BalanceSafe(current);
            if (balance.Changed &&
                !string.IsNullOrWhiteSpace(balance.Text) &&
                !string.Equals(balance.Text, current, StringComparison.Ordinal))
            {
                current = balance.Text;

                applied.Add(new AppliedCorrection(
                    "BRACKET_QUOTE_BALANCER",
                    "BracketQuoteBalancer",
                    balance.Reason ?? "Balanced brackets/quotes safely.",
                    current,
                    99));
            }

            // MeaningTitle Case Preservation (only for Title mode)
            if (mode == RewriteTargetMode.Title)
            {
                var titleFix = MeaningTitleCasePreserver.NormalizeTitleSafe(current);
                if (titleFix.Changed &&
                    !string.IsNullOrWhiteSpace(titleFix.Text) &&
                    !string.Equals(titleFix.Text, current, StringComparison.Ordinal))
                {
                    current = titleFix.Text;

                    applied.Add(new AppliedCorrection(
                        "TITLE_CASE_PRESERVE",
                        "MeaningTitleCasePreserver",
                        titleFix.Reason ?? "MeaningTitle case normalized safely.",
                        current,
                        99));
                }
            }

            return new GrammarCorrectionResult(
                OriginalText: original,
                CorrectedText: current,
                AppliedCorrections: applied,
                RemainingIssues: []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DictionaryRewriteCorrectorAdapter ApplyRewriteOnceAsync failed. Returning original.");

            try
            {
                var ctx = _rewriteContextAccessor.Current;
                await TryFlushJsonRegexHitsAsync(
                    sourceCode: NormalizeSource(ctx.SourceCode),
                    mode: ctx.Mode,
                    hitBuffer: jsonRegexHitBuffer,
                    ct: ct);
            }
            catch
            {
                // ignore
            }

            return new GrammarCorrectionResult(
                OriginalText: original,
                CorrectedText: original,
                AppliedCorrections: [],
                RemainingIssues: []);
        }
    }

    private async Task TryFlushJsonRegexHitsAsync(
        string sourceCode,
        RewriteTargetMode mode,
        Dictionary<string, long>? hitBuffer,
        CancellationToken ct)
    {
        if (_hitRepository is null)
            return;

        if (hitBuffer is null || hitBuffer.Count == 0)
            return;

        try
        {
            var src = NormalizeSource(sourceCode);
            var modeStr = MapMode(mode);

            var hits = hitBuffer
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(kv => new RewriteRuleHitUpsert
                {
                    SourceCode = src,
                    Mode = modeStr,
                    RuleType = "JsonRegex",
                    RuleKey = kv.Key,
                    HitCount = kv.Value <= 0 ? 1 : kv.Value
                })
                .ToList();

            await _hitRepository.UpsertHitsAsync(hits, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DictionaryRewriteCorrectorAdapter: JsonRegex hit flush failed (ignored).");
        }
    }

    private static string BuildJsonRegexRuleKey(GrammarPatternRule rule)
    {
        var id = (rule.Id ?? string.Empty).Trim();
        var pattern = (rule.Pattern ?? string.Empty).Trim();

        if (pattern.Length > 120)
            pattern = pattern.Substring(0, 120);

        var category = (rule.Category ?? string.Empty).Trim();
        if (category.Length > 40)
            category = category.Substring(0, 40);

        var key = $"Id={id};Cat={category};P={rule.Priority};C={rule.Confidence};Pat={pattern}";

        if (key.Length > 400)
            key = key.Substring(0, 400);

        return key;
    }

    private static string MapMode(RewriteTargetMode mode)
    {
        return mode switch
        {
            RewriteTargetMode.Definition => "Definition",
            RewriteTargetMode.Title => "Title",
            RewriteTargetMode.Example => "Example",
            _ => mode.ToString()
        };
    }

    private static Regex? GetOrCreateRegex(GrammarPatternRule rule, ILogger logger)
    {
        try
        {
            var pattern = (rule.Pattern ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            var cacheKey = $"{rule.Id}||{pattern}";
            return RegexCache.GetOrAdd(cacheKey, _ =>
            {
                try
                {
                    return new Regex(pattern, DefaultOptions, TimeSpan.FromMilliseconds(60));
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Invalid JsonRegex rule pattern. RuleId={RuleId}", rule.Id);
                    return null;
                }
            });
        }
        catch
        {
            return null;
        }
    }

    private static int ClampConfidence(int confidence)
    {
        if (confidence < 0) return 0;
        if (confidence > 100) return 100;
        return confidence;
    }

    private static string NormalizeSource(string? sourceCode)
        => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

    private static string HumanizeByMode(RewriteTargetMode mode, string text)
    {
        try
        {
            return mode switch
            {
                RewriteTargetMode.Title => text,
                RewriteTargetMode.Example => text,
                _ => text
            };
        }
        catch
        {
            return text;
        }
    }
}