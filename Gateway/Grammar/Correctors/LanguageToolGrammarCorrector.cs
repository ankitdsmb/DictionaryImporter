using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.Caching.Memory;

namespace DictionaryImporter.Gateway.Grammar.Correctors;

public sealed class LanguageToolGrammarCorrector : IGrammarCorrector, IDisposable
{
    private const int DefaultTimeoutSeconds = 30; private const int MaxInputLength = 50000; private const int MaxRetryAttempts = 3; private const int RateLimitPerMinute = 60;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CacheDurationForCommonTexts = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _rateLimitGate = new(RateLimitPerMinute, RateLimitPerMinute);
    private readonly Timer _rateLimitRefillTimer;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new();
    private readonly IMemoryCache _memoryCache;
    private readonly MemoryCacheEntryOptions _standardCacheOptions;
    private readonly MemoryCacheEntryOptions _commonTextCacheOptions;
    private readonly GrammarRuleFilter _ruleFilter;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _languageToolUrl;
    private readonly ILogger<LanguageToolGrammarCorrector>? _logger;
    private readonly IRewriteContextAccessor? _rewriteContextAccessor;
    private readonly IRewriteRuleHitRepository? _rewriteRuleHitRepository;

    private int _disposed;

    public LanguageToolGrammarCorrector(
        string languageToolUrl = "http://localhost:2026",
        ILogger<LanguageToolGrammarCorrector>? logger = null,
        IRewriteContextAccessor? rewriteContextAccessor = null,
        IRewriteRuleHitRepository? rewriteRuleHitRepository = null,
        IMemoryCache? memoryCache = null)
    {
        _languageToolUrl = languageToolUrl.TrimEnd('/');
        _logger = logger;
        _rewriteContextAccessor = rewriteContextAccessor;
        _rewriteRuleHitRepository = rewriteRuleHitRepository;
        _rateLimitRefillTimer = new Timer(
            _ =>
            {
                try
                {
                    if (_rateLimitGate.CurrentCount < RateLimitPerMinute)
                        _rateLimitGate.Release(1);
                }
                catch (SemaphoreFullException)
                {
                }
            },
            null,
            TimeSpan.FromSeconds(60.0 / RateLimitPerMinute),
            TimeSpan.FromSeconds(60.0 / RateLimitPerMinute));

        _memoryCache = memoryCache ?? new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 2048,
            CompactionPercentage = 0.25,
            ExpirationScanFrequency = TimeSpan.FromMinutes(10)
        });

        _standardCacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1,
            Priority = CacheItemPriority.Normal
        };

        _commonTextCacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDurationForCommonTexts,
            Size = 1,
            Priority = CacheItemPriority.High
        };

        _httpClient = new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = 50,
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds),
            BaseAddress = new Uri(_languageToolUrl)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DictionaryImporter", "2.0"));

        _ruleFilter = new GrammarRuleFilter
        {
            SafeAutoCorrectRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MORFOLOGIK_RULE_EN_US",
            "MORFOLOGIK_RULE_EN_GB",
            "MORFOLOGIK_RULE_EN_AU",
            "MORFOLOGIK_RULE_EN_CA",
            "MORFOLOGIK_RULE_EN_NZ",
            "UPPERCASE_SENTENCE_START",
            "EN_A_VS_AN",
            "EN_CONTRACTION_SPELLING",
            "ENGLISH_WORD_REPEAT_BEGINNING_RULE",
            "COMMA_PARENTHESIS_WHITESPACE",
            "DOUBLE_PUNCTUATION",
            "MISSING_COMMA",
            "EXTRA_SPACE",
            "UNLIKELY_OPENING_PUNCTUATION",
            "SENTENCE_WHITESPACE"
        }
        };
    }

    public async Task<GrammarCheckResult> CheckAsync(
    string text,
    string languageCode = "en-US",
    CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);

        var sw = Stopwatch.StartNew();

        var validation = ValidateInputAndPrepare(text, languageCode);
        if (!validation.IsValid)
            return new GrammarCheckResult(false, 0, [], sw.Elapsed);

        var cacheKey = GenerateCacheKey(validation.NormalizedText!, validation.NormalizedLanguage!);

        if (_memoryCache.TryGetValue(cacheKey, out GrammarCheckResult cached))
            return cached;

        var cacheLock = _cacheLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        await cacheLock.WaitAsync(ct);
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out cached))
                return cached;

            LanguageToolResponse response;
            var acquired = false;

            try
            {
                await _rateLimitGate.WaitAsync(ct);
                acquired = true;

                response = await ExecuteAdvancedLanguageToolAnalysisAsync(
                    validation.NormalizedText!,
                    validation.NormalizedLanguage!,
                    ct);
            }
            finally
            {
                if (acquired)
                    _rateLimitGate.Release();
            }

            var issues = FilterAndPrioritizeIssues(
                ConvertToEnhancedIssues(response, validation.NormalizedText!), false);

            await TryTrackLanguageToolHitsAsync(issues, ct);

            var result = new GrammarCheckResult(
                issues.Count > 0,
                issues.Count,
                issues,
                sw.Elapsed);

            _memoryCache.Set(
                cacheKey,
                result,
                IsCommonText(validation.NormalizedText!)
                    ? _commonTextCacheOptions
                    : _standardCacheOptions);

            return result;
        }
        catch
        {
            return new GrammarCheckResult(false, 0, [], sw.Elapsed);
        }
        finally
        {
            cacheLock.Release();
            if (cacheLock.CurrentCount == 1)
            {
                _cacheLocks.TryRemove(
                    new KeyValuePair<string, SemaphoreSlim>(cacheKey, cacheLock));
            }
            sw.Stop();
        }
    }

    public async Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Array.Empty<GrammarSuggestion>();

        try
        {
            var result = await CheckAsync(text, languageCode, ct);
            var suggestions = GenerateTargetedSuggestions(result.Issues, text);
            suggestions.AddRange(GenerateProactiveImprovements(text));
            return suggestions.Count > 10 ? suggestions.GetRange(0, 10) : suggestions;
        }
        catch
        {
            return GenerateBasicGuidance(text);
        }
    }

    public async Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(text))
            return new GrammarCorrectionResult(text, text, [], []);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds * 3));

            var analysis = await CheckAsync(text, languageCode, cts.Token);
            if (!analysis.HasIssues)
                return new GrammarCorrectionResult(text, text, [], []);

            var safe = IdentifySafeCorrections(analysis.Issues);
            if (safe.Count == 0)
                return new GrammarCorrectionResult(text, text, [], analysis.Issues);

            var applied = ApplyIntelligentCorrections(text, safe, cts.Token);

            // Efficient Except implementation
            var remaining = new List<GrammarIssue>(analysis.Issues.Count - safe.Count);
            var safeSet = new HashSet<GrammarIssue>(safe);
            foreach (var issue in analysis.Issues)
            {
                if (!safeSet.Contains(issue))
                {
                    remaining.Add(issue);
                }
            }

            return new GrammarCorrectionResult(text, applied.CorrectedText, applied.AppliedCorrections, remaining);
        }
        catch
        {
            return new GrammarCorrectionResult(text, text, [], []);
        }
    }

    private async Task<LanguageToolResponse> ExecuteAdvancedLanguageToolAnalysisAsync(string text, string language, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = text,
            ["language"] = language,
            ["enabledOnly"] = "false"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/check") { Content = form };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<LanguageToolResponse>(stream, JsonOptions, ct) ?? new LanguageToolResponse();
    }

    private ValidationResult ValidateInputAndPrepare(string text, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ValidationResult(false);

        var normalizedText = NormalizeText(text);
        var normalizedLanguage = NormalizeLanguage(languageCode);

        return new ValidationResult(true, normalizedText, normalizedLanguage, null);
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var trimmed = text.Trim();
        if (trimmed.Length > MaxInputLength)
            trimmed = trimmed[..MaxInputLength];

        if (!trimmed.Contains('\r'))
            return trimmed;

        return trimmed.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private List<GrammarIssue> ConvertToEnhancedIssues(LanguageToolResponse response, string originalText)
    {
        if (response.Matches == null || response.Matches.Count == 0)
            return [];

        var list = new List<GrammarIssue>(response.Matches.Count);
        var textLength = originalText.Length;

        foreach (var m in response.Matches)
        {
            var start = Math.Clamp(m.Offset, 0, textLength);
            var end = Math.Clamp(m.Offset + m.Length, start, textLength);
            var replacements = PrepareIntelligentReplacements(m.Replacements);
            var confidence = CalculateComprehensiveConfidence(m, replacements);

            list.Add(new GrammarIssue(
                start,
                end,
                m.Message ?? "Grammar issue detected",
                NormalizeCategory(m.Rule?.Category?.Id),
                replacements,
                m.Rule?.Id ?? "UNKNOWN",
                m.Rule?.Category?.Name ?? "Unknown",
                ["languagetool"],
                ExtractRichContext(originalText, start, end),
                0,
                confidence));
        }

        return list;
    }

    private List<GrammarIssue> FilterAndPrioritizeIssues(List<GrammarIssue> issues, bool forAutoCorrect)
    {
        if (issues.Count == 0) return issues;

        var filtered = new List<GrammarIssue>(issues.Count);
        foreach (var i in issues)
        {
            if (!_ruleFilter.ShouldIgnore(i) && (!forAutoCorrect || _ruleFilter.IsSafeForAutoCorrection(i)))
            {
                filtered.Add(i);
            }
        }

        filtered.Sort((a, b) => b.ConfidenceLevel.CompareTo(a.ConfidenceLevel));
        return filtered;
    }

    private static string ExtractRichContext(string text, int start, int end, int size = 100)
    {
        var s = Math.Max(0, start - size);
        var e = Math.Min(text.Length, end + size);
        return text[s..e];
    }

    private static string GenerateCacheKey(string text, string language)
    {
        var input = $"{language}:{text}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }

    private bool TryGetCachedResponse(string key, out GrammarCheckResult result)
        => _memoryCache.TryGetValue(key, out result);

    private void CacheAnalysisResult(string key, GrammarCheckResult result, string text)
        => _memoryCache.Set(key, result, IsCommonText(text) ? _commonTextCacheOptions : _standardCacheOptions);

    private static bool IsCommonText(string text)
        => text.Length < 50;

    private async Task TryTrackLanguageToolHitsAsync(IReadOnlyList<GrammarIssue> issues, CancellationToken ct)
    {
        if (_rewriteRuleHitRepository is null || _rewriteContextAccessor is null || issues.Count == 0)
            return;

        try
        {
            var ctx = _rewriteContextAccessor?.Current;
            if (ctx == null)
                return;

            var hits = issues
                .Where(i => !string.IsNullOrWhiteSpace(i.RuleId))
                .GroupBy(i => i.RuleId)
                .Select(g => new RewriteRuleHitUpsert
                {
                    SourceCode = NormalizeSource(ctx.SourceCode),
                    Mode = MapMode(ctx.Mode),
                    RuleType = "LanguageTool",
                    RuleKey = g.Key.Length > 400 ? g.Key[..400] : g.Key,
                    HitCount = g.Count()
                })
                .ToList();

            if (hits.Count > 0)
                await _rewriteRuleHitRepository.UpsertHitsAsync(hits, ct);
        }
        catch
        {
            // intentionally swallowed – telemetry must never break grammar flow
        }
    }

    private static string NormalizeSource(string? source)
        => string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source.Trim();

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

    private List<GrammarSuggestion> GenerateTargetedSuggestions(IReadOnlyList<GrammarIssue> issues, string originalText)
    {
        var result = new List<GrammarSuggestion>();

        foreach (var group in issues.GroupBy(i => i.ShortMessage).OrderByDescending(g => g.Count()).Take(5))
        {
            var issue = group.First();
            result.Add(new GrammarSuggestion(
                originalText,
                $"{issue.ShortMessage} Improvements",
                $"Found {group.Count()} issue(s) related to {issue.ShortMessage.ToLowerInvariant()}.",
                issue.ShortMessage.ToLowerInvariant()));
        }

        return result;
    }

    private List<GrammarSuggestion> GenerateProactiveImprovements(string text)
    {
        if (text.Length <= 120)
            return [];

        return
        [
            new GrammarSuggestion(
            text,
            "Improve readability",
            "Consider breaking long sentences into shorter ones.",
            "readability")
        ];
    }

    private List<GrammarSuggestion> GenerateBasicGuidance(string text)
    {
        return
        [
            new GrammarSuggestion(
            text,
            "Proofreading advice",
            "Read your text once more to catch minor grammar and spelling issues.",
            "general")
        ];
    }

    private List<GrammarIssue> IdentifySafeCorrections(IReadOnlyList<GrammarIssue> issues)
    {
        var safe = new List<GrammarIssue>(issues.Count);
        foreach (var i in issues)
        {
            if (_ruleFilter.IsSafeForAutoCorrection(i))
                safe.Add(i);
        }

        safe.Sort((a, b) => b.StartOffset.CompareTo(a.StartOffset));
        return safe;
    }

    private (string CorrectedText, List<AppliedCorrection> AppliedCorrections)
        ApplyIntelligentCorrections(string text, List<GrammarIssue> corrections, CancellationToken ct)
    {
        var sb = new StringBuilder(text);
        var applied = new List<AppliedCorrection>(corrections.Count);

        foreach (var issue in corrections)
        {
            if (ct.IsCancellationRequested || issue.Replacements.Count == 0)
                continue;

            var start = issue.StartOffset;
            var length = issue.EndOffset - issue.StartOffset;

            if (start < 0 || start + length > sb.Length)
                continue;

            // Optimization: Avoid double allocation by peeking before creating string if possible,
            // but for safety and simplicity, we extract once.
            var original = sb.ToString(start, length);
            var replacement = issue.Replacements[0];

            if (string.Equals(original, replacement, StringComparison.Ordinal))
                continue;

            sb.Remove(start, length);
            sb.Insert(start, replacement);

            applied.Add(new AppliedCorrection(
                original,
                replacement,
                issue.RuleId,
                issue.Message,
                issue.ConfidenceLevel));
        }

        return (sb.ToString(), applied);
    }

    private List<string> PrepareIntelligentReplacements(List<LanguageToolReplacement>? replacements)
    {
        if (replacements == null || replacements.Count == 0)
            return [];

        var result = new List<string>(Math.Min(replacements.Count, 5));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in replacements)
        {
            var v = r.Value?.Trim();
            if (!string.IsNullOrEmpty(v) && seen.Add(v))
            {
                result.Add(v);
                if (result.Count >= 5) break;
            }
        }

        return result;
    }

    private int CalculateComprehensiveConfidence(LanguageToolMatch match, List<string> replacements)
    {
        var score = 70;

        if (replacements.Count > 0)
            score += 10;

        if (match.Rule?.Id?.StartsWith("EN_", StringComparison.OrdinalIgnoreCase) == true)
            score += 5;

        return Math.Clamp(score, 0, 100);
    }

    private static string NormalizeLanguage(string lang)
        => string.IsNullOrWhiteSpace(lang) ? "en-US" : lang.Trim();

    private static string NormalizeCategory(string? cat)
        => string.IsNullOrWhiteSpace(cat) ? "LANGUAGETOOL" : cat.Trim().ToUpperInvariant();

    private static TimeSpan CalculateExponentialBackoff(int retry)
        => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, retry)));

    private static bool IsTransientError(HttpRequestException ex)
        => ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);

    private void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (disposing)
        {
            _rateLimitRefillTimer.Dispose();
            if (_memoryCache is MemoryCache mc)
                mc.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private sealed class GrammarRuleFilter
    {
        public IReadOnlySet<string> SafeAutoCorrectRules { get; init; }
        public int HighConfidenceThreshold { get; init; } = 85;

        public bool ShouldIgnore(GrammarIssue issue)
            => issue.ConfidenceLevel < 30;

        public bool IsSafeForAutoCorrection(GrammarIssue issue)
            => issue.ConfidenceLevel >= HighConfidenceThreshold && SafeAutoCorrectRules.Contains(issue.RuleId);
    }

    private record ValidationResult(bool IsValid, string? NormalizedText = null, string? NormalizedLanguage = null, List<string>? Errors = null);

    private sealed class LanguageToolResponse
    {
        [JsonPropertyName("matches")]
        public List<LanguageToolMatch>? Matches { get; init; } = [];
    }

    private sealed class LanguageToolMatch
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("replacements")]
        public List<LanguageToolReplacement>? Replacements { get; set; }

        [JsonPropertyName("rule")]
        public LanguageToolRule? Rule { get; set; }
    }

    private sealed class LanguageToolReplacement
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    private sealed class LanguageToolRule
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("category")]
        public LanguageToolCategory Category { get; set; } = new();
    }

    private sealed class LanguageToolCategory
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }
}