using System.Text.RegularExpressions;
using DictionaryImporter.Sources.EnglishChinese.Extractor;

namespace DictionaryImporter.Sources.Collins.Extractor;

internal class CollinsEtymologyExtractor(ILogger<CollinsEtymologyExtractor> logger) : IEtymologyExtractor
{
    private static readonly Regex LanguageOriginRegex =
        new(@"(?:源自|来自|源于|从…演变而来|via|from)\s*(?:the\s+)?(?<language>[^\s，。;]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EtymologyTextRegex =
        new(@"(?:(?:源自|来自|源于|从…演变而来)\s*(?:the\s+)?(?<language>[^\s，。;]+)|via\s+(?<language>\w+)\s+from\s+(?<origin>\w+))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> LanguageMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "拉丁语", "la" }, { "Latin", "la" },
            { "希腊语", "el" }, { "Greek", "el" },
            { "法语", "fr" }, { "French", "fr" },
            { "德语", "de" }, { "German", "de" },
            { "英语", "en" }, { "English", "en" },
            { "古英语", "ang" }, { "Old English", "ang" },
            { "中古英语", "enm" }, { "Middle English", "enm" },
            { "意大利语", "it" }, { "Italian", "it" },
            { "西班牙语", "es" }, { "Spanish", "es" },
            { "荷兰语", "nl" }, { "Dutch", "nl" },
            { "日语", "ja" }, { "Japanese", "ja" },
            { "韩语", "ko" }, { "Korean", "ko" },
            { "俄语", "ru" }, { "Russian", "ru" },
            { "阿拉伯语", "ar" }, { "Arabic", "ar" },
            { "梵语", "sa" }, { "Sanskrit", "sa" },
            { "挪威语", "no" }, { "Norwegian", "no" },
            { "丹麦语", "da" }, { "Danish", "da" },
            { "瑞典语", "sv" }, { "Swedish", "sv" },
            { "葡萄牙语", "pt" }, { "Portuguese", "pt" },
            { "凯尔特语", "cel" }, { "Celtic", "cel" }
        };

    private readonly ILogger<CollinsEtymologyExtractor> _logger = logger;

    public string SourceCode => "ENG_COLLINS";

    public EtymologyExtractionResult Extract(
        string headword,
        string definition,
        string? rawDefinition = null)
    {
        var textToAnalyze = rawDefinition ?? definition;

        if (string.IsNullOrWhiteSpace(textToAnalyze))
        {
            return new EtymologyExtractionResult
            {
                EtymologyText = null,
                LanguageCode = null,
                CleanedDefinition = definition,
                DetectionMethod = "NoText",
                SourceText = string.Empty
            };
        }

        var (etymology, languageCode) = ExtractFromText(textToAnalyze);

        return new EtymologyExtractionResult
        {
            EtymologyText = etymology,
            LanguageCode = languageCode,
            CleanedDefinition = definition,
            DetectionMethod = etymology != null ? "RegexMatch" : "NoMatch",
            SourceText = etymology ?? string.Empty
        };
    }

    public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);

        var match = EtymologyTextRegex.Match(text);
        if (match.Success)
        {
            var languageGroup = match.Groups["language"];
            var originGroup = match.Groups["origin"];

            if (languageGroup.Success)
            {
                var language = languageGroup.Value.Trim();
                var origin = originGroup.Success ? originGroup.Value.Trim() : null;

                if (LanguageMappings.TryGetValue(language, out var languageCode))
                {
                    var etymologyText = origin != null
                        ? $"via {language} from {origin}"
                        : $"from {language}";

                    return (etymologyText, languageCode);
                }

                return ($"from {language}", null);
            }
        }

        // Check for common etymology patterns
        if (text.Contains("derived from", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("comes from", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("borrowed from", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the language using a simpler pattern
            var simpleMatch = Regex.Match(text, @"(?:from|via)\s+(?:the\s+)?(?<lang>\w+)\s+(?:language)?",
                RegexOptions.IgnoreCase);

            if (simpleMatch.Success)
            {
                var language = simpleMatch.Groups["lang"].Value;
                if (LanguageMappings.TryGetValue(language, out var languageCode))
                {
                    return ($"from {language}", languageCode);
                }
            }
        }

        return (null, null);
    }
}