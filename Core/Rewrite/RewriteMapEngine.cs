using DictionaryImporter.Gateway.Redis;
using DictionaryImporter.Gateway.Rewriter;

namespace DictionaryImporter.Core.Rewrite;

public sealed class RewriteMapEngine(
    IRewriteMapRepository repository,
    IOptions<RewriteMapEngineOptions> options,
    ILogger<RewriteMapEngine> logger,
    IRewriteRuleHitRepository? hitRepository = null,
    IDistributedCacheStore? distributedCache = null // OPTIONAL
)
{
    /* ============================================================
       ORIGINAL IN-MEMORY CACHES (UNCHANGED)
       ============================================================ */

    private static readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<RewriteMapRule>>> RulesCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<string>>> StopWordsCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Regex?> RegexCache =
        new(StringComparer.Ordinal);

    private readonly IRewriteMapRepository _repository = repository;
    private readonly RewriteMapEngineOptions _options = options.Value ?? new();
    private readonly ILogger<RewriteMapEngine> _logger = logger;
    private readonly IRewriteRuleHitRepository? _hitRepository = hitRepository;
    private readonly IDistributedCacheStore? _distributedCache = distributedCache;

    /* ============================================================
       APPLY (UNCHANGED)
       ============================================================ */

    public async Task<RewriteMapResult> ApplyAsync(
        string text,
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new RewriteMapResult(text, text, Array.Empty<RewriteMapApplied>(), 0);

        var original = text;
        var current = text.Trim();
        Dictionary<string, long>? hitBuffer = null;

        try
        {
            var rules = await GetRulesAsync(sourceCode, mode, ct);
            if (rules.Count == 0)
                return new RewriteMapResult(original, current, Array.Empty<RewriteMapApplied>(), 0);

            var stopWords = await GetStopWordsAsync(sourceCode, mode, ct);
            var applied = new List<RewriteMapApplied>();

            foreach (var rule in rules)
            {
                ct.ThrowIfCancellationRequested();
                if (applied.Count >= GetMaxApplied()) break;
                if (rule is null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.FromText)) continue;
                if (IsBlockedByStopWords(rule, stopWords)) continue;

                var before = current;
                var after = ApplyOneRule(before, rule);

                if (string.IsNullOrWhiteSpace(after)) continue;
                if (string.Equals(after, before, StringComparison.Ordinal)) continue;

                current = after;

                applied.Add(new RewriteMapApplied(
                    rule.RewriteMapId,
                    rule.FromText,
                    rule.ToText,
                    rule.Priority,
                    rule.IsRegex,
                    rule.WholeWord));

                hitBuffer ??= new(StringComparer.Ordinal);
                var key = BuildRuleKey(rule);
                hitBuffer[key] = hitBuffer.TryGetValue(key, out var v) ? v + 1 : 1;
            }

            await TryFlushHitsAsync(sourceCode, mode, hitBuffer, ct);
            return new RewriteMapResult(original, current, applied, applied.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RewriteMapEngine failed. Returning original.");
            try { await TryFlushHitsAsync(sourceCode, mode, hitBuffer, ct); } catch { }
            return new RewriteMapResult(original, original, Array.Empty<RewriteMapApplied>(), 0);
        }
    }

    /* ============================================================
       RULE HITS (UNCHANGED)
       ============================================================ */

    private async Task TryFlushHitsAsync(
        string sourceCode,
        RewriteTargetMode mode,
        Dictionary<string, long>? hitBuffer,
        CancellationToken ct)
    {
        if (_hitRepository is null || hitBuffer is null || hitBuffer.Count == 0)
            return;

        var hits = hitBuffer
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(kv => new RewriteRuleHitUpsert
            {
                SourceCode = NormalizeSource(sourceCode),
                Mode = MapMode(mode),
                RuleType = "RewriteMap",
                RuleKey = kv.Key,
                HitCount = kv.Value
            })
            .ToList();

        await _hitRepository.UpsertHitsAsync(hits, ct);
    }

    /* ============================================================
       CACHE INVALIDATION (UNCHANGED + REDIS)
       ============================================================ */

    public void InvalidateCache(string? sourceCode = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                RulesCache.Clear();
                StopWordsCache.Clear();
                _ = _distributedCache?.RemoveByPrefixAsync("rewritemap:", CancellationToken.None);
                return;
            }

            sourceCode = NormalizeSource(sourceCode);

            foreach (var key in RulesCache.Keys)
                if (key.StartsWith(sourceCode + "||", StringComparison.Ordinal))
                    RulesCache.TryRemove(key, out _);

            foreach (var key in StopWordsCache.Keys)
                if (key.StartsWith(sourceCode + "||", StringComparison.Ordinal))
                    StopWordsCache.TryRemove(key, out _);

            _ = _distributedCache?.RemoveByPrefixAsync($"rewritemap:{sourceCode}:", CancellationToken.None);
        }
        catch { }
    }

    /* ============================================================
       RULE CACHE (MEMORY → REDIS → DB)
       ============================================================ */

    private async Task<IReadOnlyList<RewriteMapRule>> GetRulesAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        var ttl = GetTtl();
        if (!_options.EnableCaching || ttl <= TimeSpan.Zero)
            return await LoadRulesAsync(sourceCode, mode, ct);

        var key = $"{NormalizeSource(sourceCode)}||{mode}||rules";
        var now = DateTime.UtcNow;

        if (RulesCache.TryGetValue(key, out var mem) && mem.ExpiresUtc > now)
            return mem.Value;

        var redisKey = $"rewritemap:{NormalizeSource(sourceCode)}:{mode}:rules";
        var redis = _distributedCache is null
            ? null
            : await _distributedCache.GetAsync<IReadOnlyList<RewriteMapRule>>(redisKey, ct);

        if (redis != null)
        {
            RulesCache[key] = new CacheEntry<IReadOnlyList<RewriteMapRule>>(redis, now.Add(ttl));
            return redis;
        }

        var loaded = await LoadRulesAsync(sourceCode, mode, ct);
        RulesCache[key] = new CacheEntry<IReadOnlyList<RewriteMapRule>>(loaded, now.Add(ttl));
        _ = _distributedCache?.SetAsync(redisKey, loaded, ttl, ct);
        return loaded;
    }

    private async Task<IReadOnlyList<string>> GetStopWordsAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        var ttl = GetTtl();
        if (!_options.EnableCaching || ttl <= TimeSpan.Zero)
            return await LoadStopWordsAsync(sourceCode, mode, ct);

        var key = $"{NormalizeSource(sourceCode)}||{mode}||stopwords";
        var now = DateTime.UtcNow;

        if (StopWordsCache.TryGetValue(key, out var mem) && mem.ExpiresUtc > now)
            return mem.Value;

        var redisKey = $"rewritemap:{NormalizeSource(sourceCode)}:{mode}:stopwords";
        var redis = _distributedCache is null
            ? null
            : await _distributedCache.GetAsync<IReadOnlyList<string>>(redisKey, ct);

        if (redis != null)
        {
            StopWordsCache[key] = new CacheEntry<IReadOnlyList<string>>(redis, now.Add(ttl));
            return redis;
        }

        var loaded = await LoadStopWordsAsync(sourceCode, mode, ct);
        StopWordsCache[key] = new CacheEntry<IReadOnlyList<string>>(loaded, now.Add(ttl));
        _ = _distributedCache?.SetAsync(redisKey, loaded, ttl, ct);
        return loaded;
    }

    /* ============================================================
       LOADERS / HELPERS (UNCHANGED)
       ============================================================ */

    private async Task<IReadOnlyList<RewriteMapRule>> LoadRulesAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
        => (await _repository.GetRewriteRulesAsync(sourceCode, mode, ct))
            .Where(r => r is not null && r.Enabled && !string.IsNullOrWhiteSpace(r.FromText))
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.FromText.Length)
            .ThenBy(r => r.FromText, StringComparer.Ordinal)
            .ToList();

    private async Task<IReadOnlyList<string>> LoadStopWordsAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
        => (await _repository.GetStopWordsAsync(sourceCode, mode, ct))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length)
            .ToList();

    private static bool IsBlockedByStopWords(RewriteMapRule rule, IReadOnlyList<string> stopWords)
        => stopWords.Any(sw => string.Equals(sw, rule.FromText, StringComparison.OrdinalIgnoreCase));

    private string ApplyOneRule(string input, RewriteMapRule rule)
    {
        try
        {
            if (rule.IsRegex)
                return ApplyRegex(input, rule.FromText, rule.ToText, RegexOptions.CultureInvariant);

            if (rule.WholeWord)
                return ApplyRegex(input, $@"\b{Regex.Escape(rule.FromText)}\b", rule.ToText,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return ApplyRegex(input, Regex.Escape(rule.FromText), rule.ToText,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch { return input; }
    }

    private string ApplyRegex(string input, string pattern, string? replace, RegexOptions options)
    {
        var regex = GetOrCreateRegex(pattern, options);
        return regex != null && regex.IsMatch(input)
            ? regex.Replace(input, replace ?? string.Empty)
            : input;
    }

    private Regex? GetOrCreateRegex(string pattern, RegexOptions options)
    {
        var key = $"{pattern}||{(int)options}||{_options.RegexTimeoutMs}";
        return RegexCache.GetOrAdd(key, _ =>
        {
            try
            {
                var timeoutMs = _options.RegexTimeoutMs <= 0 ? 60 : _options.RegexTimeoutMs;
                return new Regex(pattern, options | RegexOptions.Compiled,
                    TimeSpan.FromMilliseconds(timeoutMs));
            }
            catch { return null; }
        });
    }

    private static string BuildRuleKey(RewriteMapRule rule)
    {
        var from = (rule.FromText ?? "").Trim();
        if (from.Length > 120) from = from[..120];
        return $"Id={rule.RewriteMapId};P={rule.Priority};Regex={rule.IsRegex};WW={rule.WholeWord};From={from}";
    }

    private static string MapMode(RewriteTargetMode mode) =>
        mode switch
        {
            RewriteTargetMode.Definition => "Definition",
            RewriteTargetMode.Title => "Title",
            RewriteTargetMode.Example => "Example",
            _ => mode.ToString()
        };

    private static string NormalizeSource(string? sourceCode)
        => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

    private TimeSpan GetTtl()
        => _options.CacheTtlSeconds <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(_options.CacheTtlSeconds);

    private int GetMaxApplied()
        => _options.MaxAppliedCorrections switch
        {
            <= 0 => 200,
            > 2000 => 2000,
            _ => _options.MaxAppliedCorrections
        };

    private sealed record CacheEntry<T>(T Value, DateTime ExpiresUtc);
}