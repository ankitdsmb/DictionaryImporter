using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.Core.Rewrite;

public sealed class RewriteMapEngine(
    IRewriteMapRepository repository,
    IOptions<RewriteMapEngineOptions> options,
    ILogger<RewriteMapEngine> logger,
    IRewriteRuleHitRepository? hitRepository = null) // NEW (optional dependency)
{
    private static readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<RewriteMapRule>>> RulesCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<string>>> StopWordsCache = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);

    private readonly IRewriteMapRepository _repository = repository;
    private readonly RewriteMapEngineOptions _options = options.Value ?? new RewriteMapEngineOptions();
    private readonly ILogger<RewriteMapEngine> _logger = logger;
    private readonly IRewriteRuleHitRepository? _hitRepository = hitRepository;

    public async Task<RewriteMapResult> ApplyAsync(
        string text,
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new RewriteMapResult(text, text, Array.Empty<RewriteMapApplied>(), 0);
        }

        var original = text;
        var current = text;

        // NEW: collect hits per call (aggregated)
        Dictionary<string, long>? hitBuffer = null;

        try
        {
            current = current.Trim();

            var rules = await GetRulesAsync(sourceCode, mode, ct);
            if (rules.Count == 0)
            {
                return new RewriteMapResult(original, current, Array.Empty<RewriteMapApplied>(), 0);
            }

            var stopWords = await GetStopWordsAsync(sourceCode, mode, ct);

            var applied = new List<RewriteMapApplied>();

            foreach (var rule in rules)
            {
                ct.ThrowIfCancellationRequested();

                if (applied.Count >= GetMaxApplied())
                    break;

                if (rule is null) continue;
                if (!rule.Enabled) continue;
                if (string.IsNullOrWhiteSpace(rule.FromText)) continue;

                if (IsBlockedByStopWords(rule, stopWords))
                    continue;

                var before = current;
                var after = ApplyOneRule(before, rule);

                if (string.IsNullOrWhiteSpace(after))
                    continue;

                if (string.Equals(after, before, StringComparison.Ordinal))
                    continue;

                current = after;

                applied.Add(new RewriteMapApplied(
                    RuleId: rule.RewriteMapId,
                    FromText: rule.FromText,
                    ToText: rule.ToText,
                    Priority: rule.Priority,
                    IsRegex: rule.IsRegex,
                    WholeWord: rule.WholeWord
                ));

                // NEW: buffer rule hit (only when applied changed text)
                hitBuffer ??= new Dictionary<string, long>(StringComparer.Ordinal);

                var ruleKey = BuildRuleKey(rule);
                if (hitBuffer.TryGetValue(ruleKey, out var existing))
                    hitBuffer[ruleKey] = existing + 1;
                else
                    hitBuffer[ruleKey] = 1;
            }

            // NEW: flush buffered hits (non-blocking best-effort)
            await TryFlushHitsAsync(
                sourceCode: sourceCode,
                mode: mode,
                hitBuffer: hitBuffer,
                ct: ct);

            return new RewriteMapResult(original, current, applied, applied.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RewriteMapEngine failed. Returning original.");

            // NEW: even in failure, don't risk crashes
            try
            {
                await TryFlushHitsAsync(
                    sourceCode: sourceCode,
                    mode: mode,
                    hitBuffer: hitBuffer,
                    ct: ct);
            }
            catch
            {
                // ignore
            }

            return new RewriteMapResult(original, original, Array.Empty<RewriteMapApplied>(), 0);
        }
    }

    // NEW METHOD (added)
    private async Task TryFlushHitsAsync(
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
                .OrderBy(x => x.Key, StringComparer.Ordinal) // deterministic
                .Select(kv => new RewriteRuleHitUpsert
                {
                    SourceCode = src,
                    Mode = modeStr,
                    RuleType = "RewriteMap",
                    RuleKey = kv.Key,
                    HitCount = kv.Value
                })
                .ToList();

            await _hitRepository.UpsertHitsAsync(hits, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RewriteMapEngine: hit flush failed (ignored).");
        }
    }

    // NEW METHOD (added)
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

    // NEW METHOD (added)
    private static string BuildRuleKey(RewriteMapRule rule)
    {
        // Deterministic small key (max 400 in DB).
        // Includes RuleId so we can distinguish same FromText used by multiple rules.
        var from = (rule.FromText ?? string.Empty).Trim();
        if (from.Length > 120) from = from.Substring(0, 120);

        return $"Id={rule.RewriteMapId};P={rule.Priority};Regex={rule.IsRegex};WW={rule.WholeWord};From={from}";
    }

    // NEW METHOD (added)
    public void InvalidateCache(string? sourceCode = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                RulesCache.Clear();
                StopWordsCache.Clear();
                return;
            }

            sourceCode = NormalizeSource(sourceCode);

            foreach (var key in RulesCache.Keys)
            {
                if (key.StartsWith(sourceCode + "||", StringComparison.Ordinal))
                    RulesCache.TryRemove(key, out _);
            }

            foreach (var key in StopWordsCache.Keys)
            {
                if (key.StartsWith(sourceCode + "||", StringComparison.Ordinal))
                    StopWordsCache.TryRemove(key, out _);
            }
        }
        catch
        {
            // Never crash
        }
    }

    // NEW METHOD (added)
    private async Task<IReadOnlyList<RewriteMapRule>> GetRulesAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        var enableCache = _options.EnableCaching;
        var ttl = GetTtl();

        if (!enableCache || ttl <= TimeSpan.Zero)
            return await LoadRulesAsync(sourceCode, mode, ct);

        var key = $"{NormalizeSource(sourceCode)}||{mode}||rules";
        var now = DateTime.UtcNow;

        if (RulesCache.TryGetValue(key, out var cached) && cached.ExpiresUtc > now)
            return cached.Value;

        var loaded = await LoadRulesAsync(sourceCode, mode, ct);

        RulesCache[key] = new CacheEntry<IReadOnlyList<RewriteMapRule>>(loaded, now.Add(ttl));
        return loaded;
    }

    // NEW METHOD (added)
    private async Task<IReadOnlyList<string>> GetStopWordsAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        var enableCache = _options.EnableCaching;
        var ttl = GetTtl();

        if (!enableCache || ttl <= TimeSpan.Zero)
            return await LoadStopWordsAsync(sourceCode, mode, ct);

        var key = $"{NormalizeSource(sourceCode)}||{mode}||stopwords";
        var now = DateTime.UtcNow;

        if (StopWordsCache.TryGetValue(key, out var cached) && cached.ExpiresUtc > now)
            return cached.Value;

        var loaded = await LoadStopWordsAsync(sourceCode, mode, ct);

        StopWordsCache[key] = new CacheEntry<IReadOnlyList<string>>(loaded, now.Add(ttl));
        return loaded;
    }

    // NEW METHOD (added)
    private async Task<IReadOnlyList<RewriteMapRule>> LoadRulesAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        var rules = await _repository.GetRewriteRulesAsync(sourceCode, mode, ct);

        return rules
            .Where(r => r is not null)
            .Where(r => r.Enabled)
            .Where(r => !string.IsNullOrWhiteSpace(r.FromText))
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.FromText.Length)
            .ThenBy(r => r.FromText, StringComparer.Ordinal)
            .ToList();
    }

    // NEW METHOD (added)
    private async Task<IReadOnlyList<string>> LoadStopWordsAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        var words = await _repository.GetStopWordsAsync(sourceCode, mode, ct);

        return words
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length)
            .ToList();
    }

    // NEW METHOD (added)
    private static bool IsBlockedByStopWords(RewriteMapRule rule, IReadOnlyList<string> stopWords)
    {
        if (stopWords is null || stopWords.Count == 0)
            return false;

        return stopWords.Any(sw => string.Equals(sw, rule.FromText, StringComparison.OrdinalIgnoreCase));
    }

    // NEW METHOD (added)
    private string ApplyOneRule(string input, RewriteMapRule rule)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        if (rule is null) return input;

        try
        {
            if (rule.IsRegex)
            {
                var regex = GetOrCreateRegex(rule.FromText, RegexOptions.CultureInvariant);
                if (regex is null) return input;

                if (!regex.IsMatch(input)) return input;

                return regex.Replace(input, rule.ToText ?? string.Empty);
            }

            if (rule.WholeWord)
            {
                var pattern = $@"\b{Regex.Escape(rule.FromText)}\b";
                var regex = GetOrCreateRegex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (regex is null) return input;

                if (!regex.IsMatch(input)) return input;

                return regex.Replace(input, rule.ToText ?? string.Empty);
            }

            {
                var pattern = Regex.Escape(rule.FromText);
                var regex = GetOrCreateRegex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (regex is null) return input;

                if (!regex.IsMatch(input)) return input;

                return regex.Replace(input, rule.ToText ?? string.Empty);
            }
        }
        catch
        {
            return input;
        }
    }

    // NEW METHOD (added)
    private Regex? GetOrCreateRegex(string pattern, RegexOptions options)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var cacheKey = $"{pattern}||{(int)options}||{_options.RegexTimeoutMs}";

        return RegexCache.GetOrAdd(cacheKey, _ =>
        {
            try
            {
                var timeoutMs = _options.RegexTimeoutMs <= 0 ? 60 : _options.RegexTimeoutMs;
                return new Regex(pattern, options | RegexOptions.Compiled, TimeSpan.FromMilliseconds(timeoutMs));
            }
            catch
            {
                return null;
            }
        });
    }

    // NEW METHOD (added)
    private TimeSpan GetTtl()
    {
        var seconds = _options.CacheTtlSeconds;
        if (seconds <= 0) return TimeSpan.Zero;
        return TimeSpan.FromSeconds(seconds);
    }

    // NEW METHOD (added)
    private int GetMaxApplied()
    {
        var max = _options.MaxAppliedCorrections;
        if (max <= 0) return 200;
        if (max > 2000) return 2000;
        return max;
    }

    // NEW METHOD (added)
    private static string NormalizeSource(string? sourceCode)
        => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

    private sealed record CacheEntry<T>(T Value, DateTime ExpiresUtc);
}