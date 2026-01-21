namespace DictionaryImporter.Sources.EnglishChinese.Extractor
{
    public sealed class EnglishChineseEtymologyExtractor(ILogger<EnglishChineseEtymologyExtractor> logger)
        : IEtymologyExtractor
    {
        private static readonly Regex LanguageOriginRegex =
            new(@"(?:源自|来自|源于|从…演变而来)\s*(?<language>[^\s，。]+)",
                RegexOptions.Compiled);

        private static readonly Dictionary<string, string> ChineseLanguageMappings =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "拉丁语", "la" },
                { "希腊语", "el" },
                { "法语", "fr" },
                { "德语", "de" },
                { "英语", "en" },
                { "古英语", "ang" },
                { "中古英语", "enm" },
                { "意大利语", "it" },
                { "西班牙语", "es" },
                { "荷兰语", "nl" },
                { "日语", "ja" },
                { "韩语", "ko" },
                { "俄语", "ru" },
                { "阿拉伯语", "ar" },
                { "梵语", "sa" }
            };

        private readonly ILogger<EnglishChineseEtymologyExtractor> _logger = logger;

        public string SourceCode => "ENG_CHN";

        public EtymologyExtractionResult Extract(
            string headword,
            string definition,
            string? rawDefinition = null)
        {
            return new EtymologyExtractionResult
            {
                EtymologyText = null,
                LanguageCode = null,
                CleanedDefinition = definition,
                DetectionMethod = "NotApplicable",
                SourceText = string.Empty
            };
        }

        public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (null, null);

            var match = LanguageOriginRegex.Match(text);
            if (match.Success)
            {
                var language = match.Groups["language"].Value.Trim();

                if (ChineseLanguageMappings.TryGetValue(language, out var languageCode))
                    return ($"源自{language}", languageCode);

                return ($"源自{language}", null);
            }

            return (null, null);
        }
    }
}