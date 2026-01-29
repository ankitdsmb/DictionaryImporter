using System.Globalization;

namespace DictionaryImporter.Gateway.Rewriter;

public sealed class TitleCasePreservationService : ITitleCaseProcessor, IDisposable
{
    private static readonly RegexOptions DefaultRegexOptions =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private TokenPreservationConfig _config;
    private HashSet<string> _stopWords;
    private Dictionary<string, Regex> _compiledRegex;
    private readonly object _configLock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly List<string> _configSearchPaths;

    private static readonly Regex WhitespaceRegex = new(@"\s+", DefaultRegexOptions);
    private static readonly Regex LetterCheckRegex = new(@"[a-zA-Z]", DefaultRegexOptions);

    // Static instance for backward compatibility
    private static readonly Lazy<TitleCasePreservationService> _defaultInstance =
        new(() => new TitleCasePreservationService());

    public static TitleCasePreservationService Default => _defaultInstance.Value;

    public TitleCasePreservationService()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        _configSearchPaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DictionaryImporter", "token-preservation-rules.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "domain","rewrite", "token-preservation-rules.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "domain","rewrite", "token-preservation-rules.json"),
            "token-preservation-rules.json"
        };

        LoadConfiguration();
        LoadStopWords();
    }

    public TitleCasePreservationService(string configPath) : this()
    {
        if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
        {
            _configSearchPaths.Insert(0, configPath);
            LoadConfiguration();
        }
    }

    public TitleCaseResult NormalizeTitleSafe(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new TitleCaseResult(input, false, "Empty input");

        try
        {
            var metrics = new Dictionary<string, object>();
            var original = input;
            var trimmed = input.Trim();

            // Quick analysis
            var analysis = AnalyzeText(trimmed);
            metrics.Add("Analysis", analysis);

            // Check if processing is needed
            if (!analysis.NeedsProcessing)
                return new TitleCaseResult(original, false, "Already properly cased", metrics);

            // Protect preserved tokens
            var (protectedText, tokenMap) = ProtectTokens(trimmed);
            metrics.Add("ProtectedTokens", tokenMap.Count);

            // Apply smart title casing
            var fixedTitle = ApplySmartTitleCase(protectedText);

            // Restore protected tokens
            fixedTitle = RestoreTokens(fixedTitle, tokenMap);

            // Final cleanup
            fixedTitle = NormalizeWhitespace(fixedTitle.Trim());

            var changed = !string.Equals(fixedTitle, original, StringComparison.Ordinal);
            var reason = changed ? "Title case normalized" : "No changes after processing";

            metrics.Add("TokenMapSize", tokenMap.Count);
            metrics.Add("Changed", changed);

            return new TitleCaseResult(fixedTitle, changed, reason, metrics);
        }
        catch (Exception ex)
        {
            // Log error but return original text
            Console.WriteLine($"Error in ProcessTitle: {ex.Message}");
            return new TitleCaseResult(input, false, $"Error: {ex.Message}");
        }
    }

    private TextAnalysis AnalyzeText(string text)
    {
        return new TextAnalysis
        {
            WordCount = CountWords(text),
            UppercaseRatio = CalculateUppercaseRatio(text),
            IsAllLowercase = IsAllLowercase(text),
            IsAllUppercase = IsAllUppercase(text),
            HasMixedCase = HasMixedCase(text),
            NeedsProcessing = DetermineIfNeedsProcessing(text)
        };
    }

    private bool DetermineIfNeedsProcessing(string text)
    {
        var analysis = AnalyzeText(text);

        if (analysis.IsAllUppercase && analysis.WordCount >= _config.SmartSettings.MinWordCountForShouting)
            return true;

        if (analysis.IsAllLowercase && text.Length > 3)
            return true;

        if (analysis.HasMixedCase)
            return true;

        return !IsProperlyCased(text);
    }

    private (string protectedText, Dictionary<string, string> tokenMap) ProtectTokens(string input)
    {
        var tokenMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var workingText = input;

        // Protection phases
        ProtectExactTokens(ref workingText, tokenMap);
        ProtectProperNouns(ref workingText, tokenMap);
        ProtectRegexTokens(ref workingText, tokenMap);
        ProtectPrefixSuffixWords(ref workingText, tokenMap);

        return (workingText, tokenMap);
    }

    private void ProtectExactTokens(ref string text, Dictionary<string, string> tokenMap)
    {
        var tokens = _config.Rules.AlwaysPreserveExact
            .Where(t => !string.IsNullOrEmpty(t))
            .OrderByDescending(t => t.Length)
            .ToList();

        foreach (var token in tokens)
        {
            text = ReplaceWholeWord(text, token, tokenMap, "E");
        }
    }

    private void ProtectProperNouns(ref string text, Dictionary<string, string> tokenMap)
    {
        var nouns = _config.Rules.ProperNouns
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderByDescending(n => n.Length)
            .ToList();

        foreach (var noun in nouns)
        {
            text = ProtectProperNoun(text, noun, tokenMap);
        }
    }

    private void ProtectRegexTokens(ref string text, Dictionary<string, string> tokenMap)
    {
        foreach (var regex in _compiledRegex.Values)
        {
            text = ProtectWithRegex(text, regex, tokenMap);
        }
    }

    private void ProtectPrefixSuffixWords(ref string text, Dictionary<string, string> tokenMap)
    {
        var words = ExtractWords(text);

        foreach (var word in words)
        {
            if (word.Length < 3 || tokenMap.ContainsValue(word))
                continue;

            bool shouldProtect = ShouldProtectWord(word);

            if (shouldProtect)
            {
                var placeholder = $"⟦W{tokenMap.Count + 1:00000}⟧";
                text = text.Replace(word, placeholder, StringComparison.Ordinal);
                tokenMap[placeholder] = word;
            }
        }
    }

    private bool ShouldProtectWord(string word)
    {
        // Check prefixes
        foreach (var prefix in _config.Rules.ProtectedPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                word.Length > prefix.Length)
            {
                return true;
            }
        }

        // Check suffixes
        foreach (var suffix in _config.Rules.ProtectedSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                word.Length > suffix.Length)
            {
                return true;
            }
        }

        return false;
    }

    private string ReplaceWholeWord(string text, string token, Dictionary<string, string> tokenMap, string prefix)
    {
        int index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) != -1)
        {
            if (IsWholeWord(text, index, token.Length))
            {
                var placeholder = $"⟦{prefix}{tokenMap.Count + 1:00000}⟧";
                text = text.Remove(index, token.Length).Insert(index, placeholder);
                tokenMap[placeholder] = token;
                index += placeholder.Length;
            }
            else
            {
                index += token.Length;
            }
        }
        return text;
    }

    private string ProtectProperNoun(string text, string noun, Dictionary<string, string> tokenMap)
    {
        int index = 0;
        while ((index = text.IndexOf(noun, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            // Ensure exact case match for the substring
            if (text.Substring(index, noun.Length).Equals(noun, StringComparison.Ordinal) &&
                IsWholeWord(text, index, noun.Length))
            {
                var placeholder = $"⟦P{tokenMap.Count + 1:00000}⟧";
                var originalToken = text.Substring(index, noun.Length);
                text = text.Remove(index, noun.Length).Insert(index, placeholder);
                tokenMap[placeholder] = originalToken;
                index += placeholder.Length;
            }
            else
            {
                index += noun.Length;
            }
        }

        return text;
    }

    private string ProtectWithRegex(string text, Regex regex, Dictionary<string, string> tokenMap)
    {
        var matches = regex.Matches(text)
            .Cast<Match>()
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(t => t.Length)
            .ToList();

        foreach (var token in matches)
        {
            // Skip if token is too short or already protected
            if (token.Length < 2 || tokenMap.ContainsValue(token))
                continue;

            var placeholder = $"⟦R{tokenMap.Count + 1:00000}⟧";
            text = text.Replace(token, placeholder, StringComparison.Ordinal);
            tokenMap[placeholder] = token;
        }

        return text;
    }

    private bool IsWholeWord(string text, int index, int length)
    {
        if (index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_'))
            return false;

        if (index + length < text.Length &&
            (char.IsLetterOrDigit(text[index + length]) || text[index + length] == '_'))
            return false;

        return true;
    }

    private string ApplySmartTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var tokens = SplitIntoTokens(text);
        var result = new StringBuilder();
        var context = new TitleCaseContext();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var processed = ProcessToken(token, i, tokens, context);
            result.Append(processed);
        }

        return result.ToString();
    }

    private string ProcessToken(string token, int index, List<string> tokens, TitleCaseContext context)
    {
        context.Update(token);

        if (!ContainsLetters(token))
            return token;

        var lowerToken = token.ToLowerInvariant();

        // Determine if this should be capitalized
        bool shouldCapitalize = index == 0 ||
                                context.InParentheses ||
                                context.InQuotes ||
                                !_stopWords.Contains(lowerToken) ||
                                index > 0 && tokens[index - 1] == ":";

        if (shouldCapitalize)
            return CapitalizeFirstLetter(token, context);

        return lowerToken;
    }

    private string CapitalizeFirstLetter(string word, TitleCaseContext context)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        if (_config.SmartSettings.HandleHyphenatedWords && word.Contains('-'))
        {
            return CapitalizeHyphenatedWord(word);
        }

        return CapitalizeSingleWord(word);
    }

    private string CapitalizeHyphenatedWord(string word)
    {
        var parts = word.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i == 0 || !_stopWords.Contains(parts[i].ToLowerInvariant()))
            {
                parts[i] = CapitalizeSingleWord(parts[i]);
            }
        }
        return string.Join("-", parts);
    }

    private static string CapitalizeSingleWord(string word)
    {
        var chars = word.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpper(chars[i], CultureInfo.InvariantCulture);
                break;
            }
        }
        return new string(chars);
    }

    private static List<string> SplitIntoTokens(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inWord = false;

        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-')
            {
                if (!inWord && current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                current.Append(ch);
                inWord = true;
            }
            else
            {
                if (inWord && current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                current.Append(ch);
                inWord = false;
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private string RestoreTokens(string text, Dictionary<string, string> tokenMap)
    {
        if (tokenMap.Count == 0)
            return text;

        foreach (var kvp in tokenMap.OrderByDescending(x => x.Key.Length))
        {
            text = text.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
        }

        return text;
    }

    private static string NormalizeWhitespace(string text)
    {
        return WhitespaceRegex.Replace(text, " ");
    }

    public bool ShouldPreserveToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        if (_config.Rules.AlwaysPreserveExact.Contains(token))
            return true;

        if (_config.Rules.ProperNouns.Contains(token))
            return true;

        foreach (var prefix in _config.Rules.ProtectedPrefixes)
        {
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var suffix in _config.Rules.ProtectedSuffixes)
        {
            if (token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var regex in _compiledRegex.Values)
        {
            if (regex.IsMatch(token))
                return true;
        }

        return false;
    }

    public void ReloadConfiguration()
    {
        lock (_configLock)
        {
            LoadConfiguration();
            LoadStopWords();
        }
    }

    private void LoadConfiguration()
    {
        var configPath = FindConfigFile();
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            _config = JsonSerializer.Deserialize<TokenPreservationConfig>(json, _jsonOptions)
                      ?? CreateDefaultConfig();
        }
        else
        {
            _config = CreateDefaultConfig();
            SaveDefaultConfig(configPath);
        }

        CompileRegexPatterns();
    }

    private void CompileRegexPatterns()
    {
        _compiledRegex = new Dictionary<string, Regex>();

        foreach (var kvp in _config.Rules.RegexPatterns)
        {
            try
            {
                _compiledRegex[kvp.Key] = new Regex(kvp.Value, DefaultRegexOptions);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid regex '{kvp.Key}': {ex.Message}");
            }
        }

        foreach (var pattern in _config.Rules.WordBoundaryPatterns)
        {
            try
            {
                var boundedPattern = $@"\b{Regex.Escape(pattern)}\b";
                _compiledRegex[$"Boundary_{pattern}"] = new Regex(boundedPattern, DefaultRegexOptions);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid word boundary pattern '{pattern}': {ex.Message}");
            }
        }
    }

    private void LoadStopWords()
    {
        var stopWordPath = GetStopWordsPath();
        if (File.Exists(stopWordPath))
        {
            var json = File.ReadAllText(stopWordPath);
            _stopWords = JsonSerializer.Deserialize<HashSet<string>>(json, _jsonOptions)
                         ?? CreateDefaultStopWords();
        }
        else
        {
            _stopWords = CreateDefaultStopWords();
            SaveDefaultStopWords(stopWordPath);
        }
    }

    private string FindConfigFile()
    {
        foreach (var path in _configSearchPaths)
        {
            if (File.Exists(path))
            {
                Console.WriteLine($"Using config file: {path}");
                return path;
            }
        }

        // Create directory for user config
        var userPath = _configSearchPaths[0];
        var userDir = Path.GetDirectoryName(userPath);
        if (!string.IsNullOrEmpty(userDir) && !Directory.Exists(userDir))
        {
            Directory.CreateDirectory(userDir);
        }

        return userPath;
    }

    private string GetStopWordsPath()
    {
        var configDir = Path.GetDirectoryName(FindConfigFile()) ?? ".";
        return Path.Combine(configDir, "stopwords-core.json");
    }

    private TokenPreservationConfig CreateDefaultConfig()
    {
        return new TokenPreservationConfig
        {
            Name = "Default Configuration",
            Version = "1.0.0",
            Rules = new TokenPreservationRules
            {
                AlwaysPreserveExact = new HashSet<string>
                {
                    "C#", "F#", ".NET", "ASP.NET", "C++", "Node.js",
                    "JavaScript", "TypeScript", "Python", "Java", "SQL",
                    "HTML", "CSS", "XML", "JSON", "YAML",
                    "React", "Angular", "Vue.js", "Spring", "Django",
                    "CPU", "GPU", "RAM", "ROM", "SSD", "HDD",
                    "USB", "USB-C", "HDMI", "Wi-Fi", "Bluetooth",
                    "AWS", "Azure", "GCP", "Docker", "Kubernetes"
                },
                ProperNouns = new HashSet<string>
                {
                    "John", "Mary", "James", "Robert", "Michael",
                    "William", "David", "Richard", "Joseph", "Thomas"
                },
                ProtectedPrefixes = new HashSet<string>
                {
                    "Mc", "Mac", "O'", "D'", "De", "Di", "Du",
                    "El", "La", "Le", "Van", "Von", "St.", "Dr."
                },
                ProtectedSuffixes = new HashSet<string>
                {
                    "Jr.", "Sr.", "II", "III", "IV", "Ph.D.", "M.D.",
                    "Esq.", "CPA", "CFA", "Inc.", "Ltd.", "Co."
                },
                RegexPatterns = new Dictionary<string, string>
                {
                    ["Acronym"] = @"[A-Z]{2,6}",
                    ["DottedAbbrev"] = @"(?:[A-Z]\.){1,5}[A-Z]?",
                    ["RomanNumeral"] = @"[IVXLCDM]{2,}",
                    ["VersionNumber"] = @"(?:\d+\.)+\d+(?:-[a-zA-Z0-9.-]+)?",
                    ["Email"] = @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}"
                }
            },
            SmartSettings = new SmartCasingSettings
            {
                UppercaseRatioThreshold = 0.8,
                MinWordCountForShouting = 2,
                MixedCaseBrands = new List<string>
                {
                    "iPhone", "eBay", "YouTube", "GitHub", "PayPal", "FedEx"
                }
            }
        };
    }
    private void SaveDefaultConfig(string path)
    {
        var defaultConfig = CreateDefaultConfig();
        var json = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
        File.WriteAllText(path, json);
        Console.WriteLine($"Created default config at: {path}");
    }

    private void SaveDefaultStopWords(string path)
    {
        var defaultStopWords = CreateDefaultStopWords();
        var json = JsonSerializer.Serialize(defaultStopWords, _jsonOptions);
        File.WriteAllText(path, json);
        Console.WriteLine($"Created default stop words at: {path}");
    }

    private static HashSet<string> CreateDefaultStopWords()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "are", "as", "at", "be", "but", "by",
            "for", "if", "in", "into", "is", "it", "no", "not", "of",
            "on", "or", "such", "that", "the", "their", "then", "there",
            "these", "they", "this", "to", "was", "will", "with",
            "about", "above", "after", "again", "against", "all", "am",
            "any", "because", "been", "before", "being", "below", "between",
            "both", "can", "did", "do", "does", "doing", "down", "during",
            "each", "few", "from", "further", "had", "has", "have", "having",
            "here", "hers", "herself", "him", "himself", "his", "how",
            "its", "itself", "just", "more", "most", "my", "myself",
            "now", "once", "only", "other", "our", "ours", "ourselves",
            "out", "over", "own", "same", "she", "should", "so", "some",
            "than", "too", "under", "until", "up", "very", "what",
            "when", "where", "which", "while", "who", "whom", "why"
        };
    }

    // Helper methods
    private static int CountWords(string text)
    {
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static double CalculateUppercaseRatio(string text)
    {
        int letters = 0;
        int uppercase = 0;

        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                letters++;
                if (char.IsUpper(ch))
                    uppercase++;
            }
        }

        return letters > 0 ? (double)uppercase / letters : 0;
    }

    private static bool IsAllLowercase(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsLetter(ch) && char.IsUpper(ch))
                return false;
        }
        return true;
    }

    private static bool IsAllUppercase(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsLetter(ch) && char.IsLower(ch))
                return false;
        }
        return true;
    }

    private static bool HasMixedCase(string text)
    {
        bool hasUpper = false;
        bool hasLower = false;

        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                if (char.IsUpper(ch)) hasUpper = true;
                if (char.IsLower(ch)) hasLower = true;

                if (hasUpper && hasLower)
                    return true;
            }
        }

        return false;
    }

    private bool IsProperlyCased(string text)
    {
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;

        // Check first word
        if (words[0].Length > 0 && char.IsLower(words[0][0]))
            return false;

        // Check subsequent words
        for (int i = 1; i < words.Length; i++)
        {
            var word = words[i];
            if (string.IsNullOrEmpty(word)) continue;

            if (_stopWords.Contains(word.ToLowerInvariant()))
            {
                if (word.Any(char.IsUpper))
                    return false;
                continue;
            }

            if (word.Length > 0 && char.IsLower(word[0]))
            {
                bool hasProtectedPrefix = false;
                foreach (var prefix in _config.Rules.ProtectedPrefixes)
                {
                    if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        hasProtectedPrefix = true;
                        break;
                    }
                }

                if (!hasProtectedPrefix)
                    return false;
            }
        }

        return true;
    }

    private static bool ContainsLetters(string text)
    {
        return LetterCheckRegex.IsMatch(text);
    }

    private static List<string> ExtractWords(string text)
    {
        var words = new List<string>();
        var wordChars = new List<char>();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-')
            {
                wordChars.Add(ch);
            }
            else if (wordChars.Count > 0)
            {
                words.Add(new string(wordChars.ToArray()));
                wordChars.Clear();
            }
        }

        if (wordChars.Count > 0)
            words.Add(new string(wordChars.ToArray()));

        return words;
    }

    public void Dispose()
    {
        // Clean up resources if needed
        _compiledRegex?.Clear();
        _stopWords?.Clear();
    }
}