//using DictionaryImporter.Text.Rewrite;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace DictionaryImporter.Common.Rewrite
//{
//    public sealed class DbSentenceRewriter(IRewriteRuleRepository repository)
//    {
//        private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<RewriteRule>>> _rulesCache = new();
//        private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<string>>> _stopWordsCache = new();

//        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

//        public async Task<string> RewriteAsync(string input, RewriteMode mode, CancellationToken ct)
//        {
//            if (string.IsNullOrWhiteSpace(input))
//                return input ?? string.Empty;

//            var text = input.Trim();

//            try
//            {
//                text = NormalizeSpacing(text);

//                switch (mode)
//                {
//                    case RewriteMode.GrammarFix:
//                        text = await ApplyMapAsync(text, "GrammarFix", ct);
//                        text = EnsureSentenceEnding(text);
//                        return FixFirstLetterCapitalization(text);

//                    case RewriteMode.Simplify:
//                        text = await ApplyMapAsync(text, "Simplify", ct);
//                        text = EnsureSentenceEnding(text);
//                        return FixFirstLetterCapitalization(text);

//                    case RewriteMode.Formal:
//                        text = await ApplyMapAsync(text, "Formal", ct);
//                        text = EnsureSentenceEnding(text);
//                        return FixFirstLetterCapitalization(text);

//                    case RewriteMode.Casual:
//                        text = await ApplyMapAsync(text, "Casual", ct);
//                        text = EnsureSentenceEnding(text);
//                        return FixFirstLetterCapitalization(text);

//                    case RewriteMode.Shorten:
//                        text = await ApplyShortenAsync(text, ct);
//                        text = EnsureSentenceEnding(text);
//                        return FixFirstLetterCapitalization(text);

//                    default:
//                        return input;
//                }
//            }
//            catch
//            {
//                return input; // NEVER crash
//            }
//        }

//        private async Task<string> ApplyMapAsync(string text, string mode, CancellationToken ct)
//        {
//            var rules = await GetCachedRulesAsync(mode, ct);
//            if (rules.Count == 0)
//                return text;

//            foreach (var rule in rules)
//            {
//                if (string.IsNullOrWhiteSpace(rule.FromText))
//                    continue;

//                if (rule.IsRegex)
//                {
//                    text = SafeRegexReplace(text, rule.FromText, rule.ToText);
//                    continue;
//                }

//                if (rule.IsWholeWord)
//                {
//                    var pattern = $@"\b{Regex.Escape(rule.FromText)}\b";
//                    text = SafeRegexReplace(text, pattern, rule.ToText, RegexOptions.IgnoreCase);
//                }
//                else
//                {
//                    // phrase replace (case-insensitive)
//                    text = SafeRegexReplace(text, Regex.Escape(rule.FromText), rule.ToText, RegexOptions.IgnoreCase);
//                }
//            }

//            return NormalizeSpacing(text);
//        }

//        private async Task<string> ApplyShortenAsync(string text, CancellationToken ct)
//        {
//            var stopWords = await GetCachedStopWordsAsync("Shorten", ct);
//            if (stopWords.Count == 0)
//                return text;

//            foreach (var sw in stopWords)
//            {
//                if (string.IsNullOrWhiteSpace(sw))
//                    continue;

//                // remove word/phrase safely
//                var pattern = $@"\b{Regex.Escape(sw)}\b";
//                text = SafeRegexReplace(text, pattern, "", RegexOptions.IgnoreCase);
//            }

//            return NormalizeSpacing(text);
//        }

//        private async Task<IReadOnlyList<RewriteRule>> GetCachedRulesAsync(string mode, CancellationToken ct)
//        {
//            var now = DateTime.UtcNow;

//            if (_rulesCache.TryGetValue(mode, out var cached) && cached.ExpiresUtc > now)
//                return cached.Value;

//            var rules = await repository.GetRulesAsync(mode, ct);
//            _rulesCache[mode] = new CacheEntry<IReadOnlyList<RewriteRule>>(rules, now.Add(CacheTtl));
//            return rules;
//        }

//        private async Task<IReadOnlyList<string>> GetCachedStopWordsAsync(string mode, CancellationToken ct)
//        {
//            var now = DateTime.UtcNow;

//            if (_stopWordsCache.TryGetValue(mode, out var cached) && cached.ExpiresUtc > now)
//                return cached.Value;

//            var words = await repository.GetStopWordsAsync(mode, ct);
//            _stopWordsCache[mode] = new CacheEntry<IReadOnlyList<string>>(words, now.Add(CacheTtl));
//            return words;
//        }

//        private static string NormalizeSpacing(string text)
//        {
//            if (string.IsNullOrWhiteSpace(text))
//                return text;

//            text = Regex.Replace(text, @"\s{2,}", " ");
//            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");
//            text = Regex.Replace(text, @"([!?\.])\1{1,}", "$1");
//            return text.Trim();
//        }

//        private static string EnsureSentenceEnding(string text)
//        {
//            if (string.IsNullOrWhiteSpace(text))
//                return text;

//            if (!Regex.IsMatch(text, @"[.!?]$"))
//                return text + ".";

//            return text;
//        }

//        private static string FixFirstLetterCapitalization(string text)
//        {
//            if (string.IsNullOrWhiteSpace(text))
//                return text;

//            for (int i = 0; i < text.Length; i++)
//            {
//                if (char.IsLetter(text[i]))
//                {
//                    var chars = text.ToCharArray();
//                    chars[i] = char.ToUpperInvariant(chars[i]);
//                    return new string(chars);
//                }
//            }

//            return text;
//        }

//        private static string SafeRegexReplace(string input, string pattern, string replacement, RegexOptions options = RegexOptions.None)
//        {
//            try
//            {
//                return Regex.Replace(input, pattern, replacement ?? string.Empty,
//                    options | RegexOptions.CultureInvariant,
//                    TimeSpan.FromMilliseconds(50));
//            }
//            catch
//            {
//                return input; // never crash
//            }
//        }

//        private sealed record CacheEntry<T>(T Value, DateTime ExpiresUtc);
//    }
//}
