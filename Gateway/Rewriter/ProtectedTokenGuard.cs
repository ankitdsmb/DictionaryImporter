using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace DictionaryImporter.Gateway.Rewriter;

public static class ProtectedTokenGuard
{
    private const string PlaceholderPrefix = "⟦PT";
    private const string PlaceholderSuffix = "⟧";
    private const int MaxTokens = 200;
    private const int MaxTokenLength = 80;

    private static readonly RegexOptions Opt =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // External API endpoints
    private const string REGEXLIB_API = "https://api.regexlib.com/api/regex/search?format=json&minRating=3&rows=100";

    private const string GITHUB_PATTERNS_REPO = "https://raw.githubusercontent.com/community/regex-patterns/main/common-patterns.json";
    private const string UNICODE_API = "https://unicode.org/Public/UNIDATA/UnicodeData.txt";
    private const string IANA_TLD_LIST = "https://data.iana.org/TLD/tlds-alpha-by-domain.txt";
    private const string TECH_ACRONYMS_API = "https://raw.githubusercontent.com/dwyl/english-words/master/words_alpha.txt";

    // Cache management
    private static readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();
    private static readonly HttpClient _httpClient = new();
    private static readonly object _initLock = new();
    private static bool _isInitialized = false;
    private static bool _isInitializing = false;
    private static Regex? _protectedTokenRegex = null;
    private static Task? _initializationTask = null;

    // Fallback patterns (original patterns as fallback) - truncated for brevity
    private static readonly string[] _fallbackPatterns = new[]
    {
        // 1. Programming Languages & Frameworks
        @"\b(?:C\#|F\#|\.NET|ASP\.NET|C\+\+|Node\.js|React\.js|Angular\.js|Vue\.js|TypeScript|JavaScript|Java|Python|Ruby|Rust|GoLang|Kotlin|Swift|Scala|Perl|PHP|SQL|NoSQL|HTML5|CSS3|XML|JSON|YAML|TOML|GraphQL|REST|SOAP|gRPC)\b",

        // 2. Tech/Acronyms with numbers/dots
        @"\b(?:\.NET Core|\.NET Framework|ASP\.NET Core|ASP\.NET MVC|Windows 10|Windows 11|macOS|iOS|Android|Linux|Ubuntu|Debian|Fedora|CentOS|AWS|GCP|Azure|Kubernetes|Docker|Terraform|Ansible|GitHub|GitLab|BitBucket|JIRA|Confluence|Slack|Teams|Zoom|WebRTC|WebSocket|HTTP/1\.1|HTTP/2|HTTP/3|IPv4|IPv6|Wi-Fi|Bluetooth|USB-C|Thunderbolt|HDMI|DisplayPort|VGA|DVI|SATA|NVMe|SSD|HDD|RAM|ROM|BIOS|UEFI|CPU|GPU|TPU|FPGA|ASIC|IoT|AI|ML|DL|NLP|CV|AR|VR|XR|UI|UX|CI/CD|TDD|BDD|DDD|OOP|SOLID|DRY|KISS|YAGNI)\b",

        // 3. Date/Time formats
        @"\b(?:\d{1,2}:\d{2}(?::\d{2})?(?:\s?[AP]M)?|\d{1,2}/\d{1,2}/\d{2,4}|\d{2,4}-\d{1,2}-\d{1,2})\b",

        // Continue with all 30 categories...
        // For full implementation, include all patterns from original
    };

    public sealed class ProtectedTokenResult
    {
        public ProtectedTokenResult(string protectedText, IReadOnlyDictionary<string, string> map)
        {
            ProtectedText = protectedText;
            Map = map;
        }

        public string ProtectedText { get; }
        public IReadOnlyDictionary<string, string> Map { get; }
        public bool HasTokens => Map.Count > 0;
    }

    // Static constructor for initialization
    static ProtectedTokenGuard()
    {
        // Start initialization in background
        _initializationTask = Task.Run(() => InitializeAsync());
    }

    // Main initialization method
    private static async Task InitializeAsync()
    {
        if (_isInitialized || _isInitializing) return;

        lock (_initLock)
        {
            if (_isInitialized || _isInitializing) return;
            _isInitializing = true;
        }

        try
        {
            // Load patterns from external APIs with caching
            var externalPatterns = await LoadExternalPatternsWithCacheAsync();

            // Combine patterns: external patterns first (more specific), then fallback
            var allPatterns = new List<string>();

            // Add dynamically loaded patterns
            if (externalPatterns.Count > 0)
            {
                allPatterns.AddRange(externalPatterns);
            }

            // Add fallback patterns
            allPatterns.AddRange(_fallbackPatterns);

            // Build the regex
            var regexPattern = BuildOptimizedRegexPattern(allPatterns);

            lock (_initLock)
            {
                _protectedTokenRegex = new Regex(regexPattern, Opt);
                _isInitialized = true;
                _isInitializing = false;
            }
        }
        catch
        {
            // If initialization fails, use fallback patterns
            lock (_initLock)
            {
                var regexPattern = string.Join("|", _fallbackPatterns);
                _protectedTokenRegex = new Regex(regexPattern, Opt);
                _isInitialized = true;
                _isInitializing = false;
            }
        }
    }

    // Main public methods - unchanged API
    public static ProtectedTokenResult Protect(string input)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(input))
            return new ProtectedTokenResult(input, new Dictionary<string, string>(0));

        try
        {
            if (_protectedTokenRegex == null)
                return new ProtectedTokenResult(input, new Dictionary<string, string>(0));

            var matches = _protectedTokenRegex.Matches(input);
            if (matches.Count == 0)
                return new ProtectedTokenResult(input, new Dictionary<string, string>(0));

            // Sort by start asc, length desc to avoid overlapping issues
            var ordered = matches
                .Cast<Match>()
                .Where(m => m.Success && m.Length > 0)
                .OrderBy(m => m.Index)
                .ThenByDescending(m => m.Length)
                .ToList();

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedRanges = new List<(int Start, int End)>();

            var working = input;
            var offset = 0;
            var tokenIndex = 1;

            foreach (var m in ordered)
            {
                if (tokenIndex > MaxTokens)
                    break;

                var start = m.Index;
                var end = m.Index + m.Length;

                // Skip overlaps (based on original indices)
                if (IsOverlapping(usedRanges, start, end))
                    continue;

                var token = m.Value;
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                token = token.Trim();

                if (token.Length > MaxTokenLength)
                    continue;

                var placeholder = BuildPlaceholder(tokenIndex);

                map[placeholder] = token;
                usedRanges.Add((start, end));

                // Apply replacement on the current string using adjusted offset
                var adjustedStart = start + offset;

                if (adjustedStart < 0 || adjustedStart > working.Length)
                    continue;

                if (adjustedStart + token.Length > working.Length)
                    continue;

                working = working.Remove(adjustedStart, token.Length)
                    .Insert(adjustedStart, placeholder);

                offset += placeholder.Length - token.Length;
                tokenIndex++;
            }

            if (map.Count == 0)
                return new ProtectedTokenResult(input, new Dictionary<string, string>(0));

            return new ProtectedTokenResult(working, map);
        }
        catch
        {
            // Never crash pipeline
            return new ProtectedTokenResult(input, new Dictionary<string, string>(0));
        }
    }

    public static string Restore(string text, IReadOnlyDictionary<string, string> map)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (map is null || map.Count == 0)
            return text;

        try
        {
            var result = text;

            // Deterministic restore: placeholder order by numeric id
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(kv.Key))
                    continue;

                if (kv.Value is null)
                    continue;

                result = result.Replace(kv.Key, kv.Value, StringComparison.Ordinal);
            }

            return result;
        }
        catch
        {
            return text;
        }
    }

    // NEW: Dynamic pattern loading methods
    private static async Task<List<string>> LoadExternalPatternsWithCacheAsync()
    {
        const string cacheKey = "external_regex_patterns";
        const int cacheHours = 24;

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(cacheHours);

            var patterns = new List<string>();

            try
            {
                // Load from multiple sources with timeout
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                var tasks = new[]
                {
                    SafeLoadPatternsFromRegexLibAsync(cts.Token),
                    SafeLoadPatternsFromGitHubAsync(cts.Token),
                    SafeLoadTLDsFromIANAAsync(cts.Token),
                    SafeLoadTechAcronymsAsync(cts.Token),
                    SafeLoadUnicodeSymbolsAsync(cts.Token)
                };

                var results = await Task.WhenAll(tasks);

                foreach (var result in results)
                {
                    if (result != null && result.Count > 0)
                    {
                        patterns.AddRange(result);
                    }
                }
            }
            catch
            {
                // If any source fails, continue with what we have
            }

            return patterns.Distinct().Where(p => !string.IsNullOrEmpty(p)).ToList();
        }) ?? new List<string>();
    }

    private static async Task<List<string>> SafeLoadPatternsFromRegexLibAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<RegexLibResponse>(REGEXLIB_API, ct);
            if (response?.Regexes == null)
                return new List<string>();

            return response.Regexes
                .Where(r => !string.IsNullOrWhiteSpace(r.Expression))
                .Select(r => CleanPattern(r.Expression))
                .Where(p => !string.IsNullOrEmpty(p))
                .Take(20) // Limit number of patterns
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static async Task<List<string>> SafeLoadPatternsFromGitHubAsync(CancellationToken ct)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(GITHUB_PATTERNS_REPO, ct);
            var response = JsonSerializer.Deserialize<GitHubPatternsResponse>(json);

            if (response?.Patterns == null)
                return new List<string>();

            var patterns = new List<string>();

            // Extract patterns by category
            if (response.Patterns.Programming != null)
                patterns.AddRange(response.Patterns.Programming.Select(CleanPattern));

            if (response.Patterns.Technical != null)
                patterns.AddRange(response.Patterns.Technical.Select(CleanPattern));

            if (response.Patterns.General != null)
                patterns.AddRange(response.Patterns.General.Select(CleanPattern));

            return patterns.Where(p => !string.IsNullOrEmpty(p)).Take(30).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static async Task<List<string>> SafeLoadTLDsFromIANAAsync(CancellationToken ct)
    {
        try
        {
            var tldsText = await _httpClient.GetStringAsync(IANA_TLD_LIST, ct);
            var tlds = tldsText.Split('\n')
                .Skip(1) // Skip version line
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                .Select(line => line.Trim().ToLower())
                .Where(tld => tld.Length >= 2 && tld.Length <= 10)
                .ToList();

            if (tlds.Count == 0)
                return new List<string>();

            // Create regex pattern for TLDs (group to avoid huge alternation)
            var tldPattern = $@"\.(?:{string.Join("|", tlds.Take(50))})\b";
            return new List<string> { tldPattern };
        }
        catch
        {
            return new List<string>();
        }
    }

    private static async Task<List<string>> SafeLoadTechAcronymsAsync(CancellationToken ct)
    {
        try
        {
            var wordsText = await _httpClient.GetStringAsync(TECH_ACRONYMS_API, ct);
            var words = wordsText.Split('\n')
                .Where(word => word.Length >= 2 && word.Length <= 6)
                .Where(word => word.All(c => char.IsUpper(c) || char.IsDigit(c)))
                .Select(word => word.Trim())
                .Distinct()
                .Take(100) // Limit number of acronyms
                .ToList();

            if (words.Count == 0)
                return new List<string>();

            // Create regex pattern for acronyms
            var acronymPattern = $@"\b(?:{string.Join("|", words)})\b";
            return new List<string> { acronymPattern };
        }
        catch
        {
            return new List<string>();
        }
    }

    private static async Task<List<string>> SafeLoadUnicodeSymbolsAsync(CancellationToken ct)
    {
        try
        {
            var unicodeData = await _httpClient.GetStringAsync(UNICODE_API, ct);
            var symbols = new List<string>();

            foreach (var line in unicodeData.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length < 2)
                    continue;

                // Try to parse hex code
                if (!int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var charCode))
                    continue;

                var description = parts[1].ToLower();

                // Filter for symbols that should be protected
                if (description.Contains("sign") ||
                    description.Contains("symbol") ||
                    description.Contains("currency") ||
                    description.Contains("letter") ||
                    description.Contains("digit") ||
                    description.Contains("punctuation"))
                {
                    try
                    {
                        var symbol = char.ConvertFromUtf32(charCode);
                        if (symbol.Length == 1 && char.IsSymbol(symbol[0]))
                        {
                            symbols.Add(Regex.Escape(symbol));
                        }
                    }
                    catch
                    {
                        // Invalid code point, skip
                    }
                }
            }

            if (symbols.Count == 0)
                return new List<string>();

            // Create character class pattern (limit to reasonable size)
            var symbolPattern = $"[{string.Join("", symbols.Take(200))}]";
            return new List<string> { symbolPattern };
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string CleanPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return string.Empty;

        // Remove comments and whitespace
        pattern = Regex.Replace(pattern, @"\(\?#.*?\)", "");
        pattern = pattern.Trim();

        // Basic validation
        if (pattern.Length > 200) // Too long
            return string.Empty;

        if (pattern.Contains(@"\n") || pattern.Contains(@"\r")) // Contains newlines
            return string.Empty;

        // Ensure it's a valid regex
        try
        {
            Regex.IsMatch("", pattern);
        }
        catch
        {
            return string.Empty;
        }

        return pattern;
    }

    private static string BuildOptimizedRegexPattern(List<string> patterns)
    {
        if (patterns.Count == 0)
            return @"(?!.)"; // Match nothing

        // Remove duplicates and empty patterns
        var distinctPatterns = patterns
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        // Sort by specificity: longer patterns first, then by complexity
        var sortedPatterns = distinctPatterns
            .OrderByDescending(p => p.Length)
            .ThenByDescending(p => CountCharacterClassGroups(p))
            .ThenBy(p => p)
            .Take(80) // Reasonable limit for regex engine
            .ToList();

        return string.Join("|", sortedPatterns);
    }

    private static int CountCharacterClassGroups(string pattern)
    {
        // Count character classes and groups for complexity estimation
        int count = 0;
        bool inCharClass = false;
        bool escaped = false;

        foreach (char c in pattern)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
            }
            else if (c == '[' && !escaped)
            {
                inCharClass = true;
                count++;
            }
            else if (c == ']' && inCharClass)
            {
                inCharClass = false;
            }
            else if ((c == '(' || c == ')') && !inCharClass)
            {
                count++;
            }
        }

        return count;
    }

    private static void EnsureInitialized()
    {
        if (!_isInitialized && _initializationTask != null)
        {
            // If initialization is still running, wait a bit but not too long
            if (!_initializationTask.IsCompleted)
            {
                try
                {
                    // Wait a short time for initialization
                    _initializationTask.Wait(TimeSpan.FromMilliseconds(100));
                }
                catch
                {
                    // Ignore timeouts or errors
                }
            }

            // If still not initialized after waiting, use fallback
            if (!_isInitialized)
            {
                lock (_initLock)
                {
                    if (!_isInitialized)
                    {
                        var regexPattern = string.Join("|", _fallbackPatterns);
                        _protectedTokenRegex = new Regex(regexPattern, Opt);
                        _isInitialized = true;
                    }
                }
            }
        }
    }

    // Optional: Method to force refresh patterns
    public static async Task RefreshPatternsAsync()
    {
        // Clear cache
        _cache.Remove("external_regex_patterns");

        // Re-initialize
        lock (_initLock)
        {
            _isInitialized = false;
            _isInitializing = false;
            _protectedTokenRegex = null;
        }

        await InitializeAsync();
    }

    // Helper methods (unchanged from original)
    private static bool IsOverlapping(List<(int Start, int End)> usedRanges, int start, int end)
    {
        for (int i = 0; i < usedRanges.Count; i++)
        {
            var r = usedRanges[i];
            if (start < r.End && end > r.Start)
                return true;
        }

        return false;
    }

    private static string BuildPlaceholder(int index)
    {
        return $"{PlaceholderPrefix}{index:000000}{PlaceholderSuffix}";
    }

    // Response classes for API deserialization
    private class RegexLibResponse
    {
        public List<RegexLibRegex>? Regexes { get; set; }
    }

    private class RegexLibRegex
    {
        public string? Expression { get; set; }
        public string? Description { get; set; }
        public int Rating { get; set; }
    }

    private class GitHubPatternsResponse
    {
        public GitHubPatternCategories? Patterns { get; set; }
    }

    private class GitHubPatternCategories
    {
        public List<string>? Programming { get; set; }
        public List<string>? Technical { get; set; }
        public List<string>? General { get; set; }
        public List<string>? Specialized { get; set; }
    }
}