using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Common
{
    public class PartOfSpeechConfig
    {
        public Dictionary<string, string> Abbreviations { get; set; } = new();
        public HashSet<string> FullWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> NormalizationMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class DynamicPOSProvider
    {
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(1);
        private readonly HttpClient _httpClient;

        public DynamicPOSProvider(IConfiguration configuration, IMemoryCache cache, HttpClient? httpClient = null)
        {
            _configuration = configuration;
            _cache = cache;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PartOfSpeechConfig> GetPOSConfigAsync(string language = "en", string dictionaryType = "gutenberg")
        {
            var cacheKey = $"pos_config_{language}_{dictionaryType}";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheDuration;

                // Try to load from configuration
                var config = _configuration.GetSection($"Dictionary:POS:{language}:{dictionaryType}")
                    .Get<PartOfSpeechConfig>();

                if (config != null && (config.FullWords.Any() || config.Abbreviations.Any()))
                    return config;

                // Try to fetch dynamically based on dictionary type
                config = await FetchPOSConfigAsync(language, dictionaryType);

                // Ensure we have at least basic defaults
                if (!config.FullWords.Any())
                {
                    config = GetDefaultPOSConfig(dictionaryType);
                }

                return config;
            });
        }

        private async Task<PartOfSpeechConfig> FetchPOSConfigAsync(string language, string dictionaryType)
        {
            var config = new PartOfSpeechConfig();

            try
            {
                // Strategy: Different sources for different dictionary types
                switch (dictionaryType.ToLowerInvariant())
                {
                    case "gutenberg":
                    case "webster":
                        // Gutenberg/Webster specific POS terms
                        await LoadGutenbergPOSConfig(config);
                        break;

                    case "oxford":
                        // Oxford dictionary style
                        await LoadOxfordPOSConfig(config);
                        break;

                    //case "wiktionary":
                    //    // Wiktionary style
                    //    await LoadWiktionaryPOSConfig(config);
                    //    break;

                    default:
                        // Generic English
                        await LoadGutenbergPOSConfig(config);
                        break;
                }

                // Also load from any configured URLs
                var urls = _configuration.GetSection($"Dictionary:POS:{language}:Urls")
                    .Get<string[]>();

                if (urls != null)
                {
                    foreach (var url in urls)
                    {
                        await MergePOSFromUrl(config, url);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching POS config: {ex.Message}");
            }

            return config;
        }

        private async Task LoadGutenbergPOSConfig(PartOfSpeechConfig config)
        {
            // Gutenberg/Webster 1913 dictionary specific POS terms
            config.FullWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "noun", "verb", "adjective", "adverb", "preposition",
            "conjunction", "interjection", "pronoun", "article", "determiner",
            "participle", "gerund", "infinitive", "substantive",
            "numeral", "particle"
        };

            config.Abbreviations = new Dictionary<string, string>
            {
                ["n."] = "noun",
                ["v."] = "verb",
                ["v.t."] = "verb",
                ["v.i."] = "verb",
                ["a."] = "adjective",
                ["adj."] = "adjective",
                ["adv."] = "adverb",
                ["prep."] = "preposition",
                ["conj."] = "conjunction",
                ["interj."] = "interjection",
                ["pron."] = "pronoun",
                ["art."] = "article",
                ["det."] = "determiner",
                ["p."] = "participle",
                ["ger."] = "gerund",
                ["inf."] = "infinitive",
                ["subst."] = "substantive",
                ["num."] = "numeral",
                ["part."] = "particle"
            };

            // Normalization map for variations
            config.NormalizationMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["n"] = "noun",
                ["v"] = "verb",
                ["vt"] = "verb",
                ["vi"] = "verb",
                ["a"] = "adjective",
                ["adj"] = "adjective",
                ["adv"] = "adverb",
                ["prep"] = "preposition",
                ["conj"] = "conjunction",
                ["interj"] = "interjection",
                ["pron"] = "pronoun",
                ["art"] = "article",
                ["det"] = "determiner",
                ["p"] = "participle",
                ["ger"] = "gerund",
                ["inf"] = "infinitive",
                ["subst"] = "substantive",
                ["num"] = "numeral",
                ["part"] = "particle",
                ["vb"] = "verb",
                ["advb"] = "adverb",
                ["pre"] = "preposition",
                ["int"] = "interjection",
                ["pro"] = "pronoun"
            };
        }

        private async Task LoadOxfordPOSConfig(PartOfSpeechConfig config)
        {
            // Oxford dictionary style
            config.FullWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "noun", "verb", "adjective", "adverb", "preposition",
            "conjunction", "interjection", "pronoun", "determiner",
            "numeral", "particle", "auxiliary", "modal", "article",
            "predeterminer", "exclamation", "symbol", "prefix", "suffix"
        };

            config.Abbreviations = new Dictionary<string, string>
            {
                ["n"] = "noun",
                ["v"] = "verb",
                ["adj"] = "adjective",
                ["adv"] = "adverb",
                ["prep"] = "preposition",
                ["conj"] = "conjunction",
                ["int"] = "interjection",
                ["pron"] = "pronoun",
                ["det"] = "determiner",
                ["num"] = "numeral",
                ["part"] = "particle",
                ["aux"] = "auxiliary",
                ["modal"] = "modal",
                ["art"] = "article",
                ["predet"] = "predeterminer",
                ["excl"] = "exclamation",
                ["sym"] = "symbol",
                ["pref"] = "prefix",
                ["suf"] = "suffix"
            };
        }

        private async Task MergePOSFromUrl(PartOfSpeechConfig config, string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var externalConfig = JsonSerializer.Deserialize<PartOfSpeechConfig>(content);

                    if (externalConfig != null)
                    {
                        // Merge full words
                        foreach (var word in externalConfig.FullWords)
                        {
                            config.FullWords.Add(word);
                        }

                        // Merge abbreviations
                        foreach (var kvp in externalConfig.Abbreviations)
                        {
                            if (!config.Abbreviations.ContainsKey(kvp.Key))
                                config.Abbreviations[kvp.Key] = kvp.Value;
                        }

                        // Merge normalization
                        foreach (var kvp in externalConfig.NormalizationMap)
                        {
                            if (!config.NormalizationMap.ContainsKey(kvp.Key))
                                config.NormalizationMap[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch
            {
                // Silently fail - we have defaults
            }
        }

        private PartOfSpeechConfig GetDefaultPOSConfig(string dictionaryType)
        {
            var config = new PartOfSpeechConfig();

            // Universal English POS terms (works for most dictionaries)
            config.FullWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "noun", "verb", "adjective", "adverb", "preposition",
            "conjunction", "interjection", "pronoun", "article",
            "determiner", "numeral", "particle", "auxiliary"
        };

            config.Abbreviations = new Dictionary<string, string>
            {
                ["n."] = "noun",
                ["v."] = "verb",
                ["adj."] = "adjective",
                ["adv."] = "adverb",
                ["prep."] = "preposition",
                ["conj."] = "conjunction",
                ["interj."] = "interjection",
                ["pron."] = "pronoun",
                ["art."] = "article",
                ["det."] = "determiner"
            };

            return config;
        }

        // Method to generate regex pattern from the config
        public string GeneratePOSRegexPattern(PartOfSpeechConfig config)
        {
            if (config == null || !config.FullWords.Any())
                return GetDefaultPOSRegex();

            // Escape regex special characters in POS terms
            var escapedTerms = config.FullWords
                .Select(term => Regex.Escape(term))
                .ToList();

            // Create pattern: \b(term1|term2|term3)\b
            var pattern = $@"\b({string.Join("|", escapedTerms)})\b";

            return pattern;
        }

        public string GenerateAbbreviationRegexPattern(PartOfSpeechConfig config)
        {
            if (config == null || !config.Abbreviations.Any())
                return GetDefaultAbbreviationRegex();

            // Escape regex special characters in abbreviations
            var escapedAbbrs = config.Abbreviations.Keys
                .Select(abbr => Regex.Escape(abbr))
                .ToList();

            // Create pattern for abbreviations (may include dots)
            var pattern = $@"\b({string.Join("|", escapedAbbrs)})\b";

            return pattern;
        }

        private string GetDefaultPOSRegex()
        {
            return @"\b(noun|verb|adjective|adverb|preposition|conjunction|interjection|pronoun|article|determiner)\b";
        }

        private string GetDefaultAbbreviationRegex()
        {
            return @"\b(v\.t\.|v\.i\.|v\.|n\.|a\.|adj\.|adv\.|prep\.|conj\.|interj\.|pron\.|art\.|det\.)\b";
        }
    }
}