using DictionaryImporter.Core.Text;
using System.Globalization;
using System.Net;

namespace DictionaryImporter.Common
{
    public static class Helper
    {

        public const int MAX_RECORDS_PER_SOURCE = 1000;

        #region Bilingual / Non-English Preservation

        private static readonly HashSet<string> BilingualSources = new(StringComparer.OrdinalIgnoreCase)
        {
            "ENG_CHN",
            "CENTURY21",
            "ENG_COLLINS"
        };

        public static bool ShouldPreserveNonEnglishText(string? sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return false;

            return BilingualSources.Contains(sourceCode);
        }

        public static string PreserveBilingualContent(string text, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (!ShouldPreserveNonEnglishText(sourceCode))
                return text;

            var t = text;

            if (t.Contains('&'))
                t = WebUtility.HtmlDecode(t);

            t = Regex.Replace(t, @"\s+", " ").Trim();

            return t;
        }

        #endregion

        #region Entry Validation (Definition Normalization)

        public static string NormalizeDefinitionForSource(string definition, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            if (ShouldPreserveNonEnglishText(sourceCode))
                return PreserveBilingualContent(definition, sourceCode);

            return NormalizeDefinition(definition);
        }

        public static string NormalizeDefinition(string definition, string? sourceCode = null)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            if (!string.IsNullOrWhiteSpace(sourceCode))
                return NormalizeDefinitionForSource(definition, sourceCode);

            var normalized = definition
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        #endregion

        #region Shared Validation and Extraction

        public static string NormalizeWordWithSourceContext(string word, string sourceCode)
        {
            return NormalizeWordPreservingLanguage(word, sourceCode);
        }

        public static string? ExtractJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                return !string.IsNullOrWhiteSpace(value) ? value : null;
            }

            return null;
        }

        public static JsonElement.ArrayEnumerator? ExtractJsonArray(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
                return property.EnumerateArray();

            return null;
        }

        public static bool ContainsLanguageMarker(string text, params string[] languages)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var language in languages)
            {
                if (text.Contains(language, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion

        #region Source Processing Control

        private static readonly ConcurrentDictionary<string, ProcessingState> _sourceProcessingState =
            new(StringComparer.OrdinalIgnoreCase);

        private sealed class ProcessingState
        {
            public int Count;
            public int LimitReachedLogged;
        }

        public static bool ShouldContinueProcessing(string sourceCode, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return false;

            var state = _sourceProcessingState.GetOrAdd(sourceCode, _ => new ProcessingState());

            if (Volatile.Read(ref state.Count) >= MAX_RECORDS_PER_SOURCE)
            {
                LogLimitOnce(logger, sourceCode);
                return false;
            }

            var newCount = Interlocked.Increment(ref state.Count);

            if (newCount > MAX_RECORDS_PER_SOURCE)
            {
                LogLimitOnce(logger, sourceCode);
                return false;
            }

            return true;
        }

        private static void LogLimitOnce(ILogger? logger, string sourceCode)
        {
            if (logger == null)
                return;

            var state = _sourceProcessingState.GetOrAdd(sourceCode, _ => new ProcessingState());

            if (Volatile.Read(ref state.LimitReachedLogged) == 0)
            {
                if (Interlocked.Exchange(ref state.LimitReachedLogged, 1) == 0)
                {
                    logger.LogInformation(
                        "Reached maximum of {MaxRecords} records for {Source} source",
                        MAX_RECORDS_PER_SOURCE, sourceCode);
                }
            }
        }

        public static void ResetProcessingState(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return;

            _sourceProcessingState.TryRemove(sourceCode, out _);
        }

        public static int GetCurrentCount(string sourceCode)
        {
            return _sourceProcessingState.TryGetValue(sourceCode, out var state) ? state.Count : 0;
        }

        #endregion

        #region Webster and General Parser

        public static string? ExtractSection(string definition, string marker)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            if (string.IsNullOrWhiteSpace(marker))
                return null;

            var startIndex = definition.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
                return null;

            startIndex += marker.Length;

            var endIndex = definition.IndexOf("【", startIndex, StringComparison.Ordinal);
            if (endIndex < 0)
                endIndex = definition.Length;

            return definition.Substring(startIndex, endIndex - startIndex).Trim();
        }

        #endregion

        #region Helper Creation

        public static ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry?.Word ?? "unnamed sense",
                Definition = entry?.Definition ?? string.Empty,
                RawFragment = entry?.RawFragment ?? string.Empty,
                SenseNumber = entry?.SenseNumber ?? 1,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = new List<string>(),
                Alias = null
            };
        }

        #endregion

        #region Logging and Error Handling

        public static void LogProgress(ILogger logger, string sourceCode, int count)
        {
            if (logger == null)
                return;

            if (count % 10 == 0)
            {
                logger.LogInformation(
                    "{Source} processing progress: {Count} records processed",
                    sourceCode, count);
            }
        }

        public static void HandleError(ILogger logger, Exception ex, string sourceCode, string operation)
        {
            logger.LogError(ex, "Error {Operation} for {Source} entry", operation, sourceCode);
            ResetProcessingState(sourceCode);
        }

        #endregion

        #region Domain Extraction

        public static string? ExtractProperDomain(string sourceCode, string? rawDomain, string definition)
        {
            if (string.IsNullOrWhiteSpace(rawDomain))
                return null;

            var domain = rawDomain.Trim();

            switch (sourceCode)
            {
                case "ENG_OXFORD":
                    var oxfordMatch = Regex.Match(definition ?? string.Empty, @"^\(([^)]+)\)");
                    if (oxfordMatch.Success)
                    {
                        var oxfordDomain = oxfordMatch.Groups[1].Value.Trim();
                        oxfordDomain = oxfordDomain.Split('.')[0].Trim();
                        return oxfordDomain.Length <= 100 ? oxfordDomain : oxfordDomain.Substring(0, 100);
                    }
                    return null;

                case "ENG_COLLINS":
                    if (domain.StartsWith("【语域标签】：", StringComparison.OrdinalIgnoreCase))
                    {
                        domain = domain.Substring("【语域标签】：".Length).Trim();

                        var parts = domain.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                            return parts[0].Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(definition))
                    {
                        if (definition.Contains("主美") || definition.Contains("美式")) return "US";
                        if (definition.Contains("主英") || definition.Contains("英式")) return "UK";
                        if (definition.Contains("正式")) return "FORMAL";
                        if (definition.Contains("非正式")) return "INFORMAL";
                    }

                    return null;

                case "STRUCT_JSON":
                case "KAIKKI":
                    domain = Regex.Replace(domain, @"[<>\(\)]", "").Trim();
                    return domain.Length <= 50 ? domain : domain.Substring(0, 50);

                case "GUT_WEBSTER":
                    var gutenbergMatch = Regex.Match(domain, @"[<\(]([^>)]+)[>\)]");
                    if (gutenbergMatch.Success)
                        return gutenbergMatch.Groups[1].Value.Trim().TrimEnd('.');

                    return null;

                case "CENTURY21":
                    return null;

                case "ENG_CHN":
                    var chnMatch = Regex.Match(definition ?? string.Empty, @"〔([^〕]+)〕");
                    if (chnMatch.Success)
                        return chnMatch.Groups[1].Value.Trim();
                    return null;

                default:
                    return CleanDomainGeneric(domain);
            }
        }

        private static string? CleanDomainGeneric(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return null;

            domain = domain.Trim();

            if (domain.Length > 100)
                return null;

            var definitionIndicators = new[] { "hours", "days", "weeks", "minutes", "seconds", "o'clock" };
            if (definitionIndicators.Any(ind => domain.Contains(ind, StringComparison.OrdinalIgnoreCase)))
                return null;

            domain = Regex.Replace(domain, @"[\u4e00-\u9fff]", "").Trim();

            return string.IsNullOrWhiteSpace(domain) ? null : domain;
        }

        #endregion

        #region Regex Patterns

        private static readonly Regex HasEnglishLetter = new("[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex IpaRegex = new(@"/[^/]+/", RegexOptions.Compiled);
        private static readonly Regex EnglishSyllableRegex = new(@"^\s*[A-Za-z]+(?:·[A-Za-z]+)+\s*", RegexOptions.Compiled);

        private static readonly Regex PosRegex = new(
            @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion

        #region Headword Detection

        public static bool IsHeadword(string line, int maxLength = 40, bool requireUppercase = true)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var text = line.Trim();

            if (text.Length > maxLength)
                return false;

            if (requireUppercase && !text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal))
                return false;

            if (!text.Any(char.IsLetter))
                return false;

            return true;
        }

        public static bool ContainsEnglishLetters(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && HasEnglishLetter.IsMatch(text);
        }

        #endregion

        #region Text Cleaning and Normalization

        public static string RemoveIpaMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return IpaRegex.Replace(text, string.Empty);
        }

        public static string RemoveSyllableMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return EnglishSyllableRegex.Replace(text, string.Empty);
        }

        public static string RemovePosMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return PosRegex.Replace(text, string.Empty);
        }

        public static string RemoveHeadwordFromDefinition(string definition, string headword)
        {
            if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(headword))
                return definition ?? string.Empty;

            var escapedHeadword = Regex.Escape(headword);

            return Regex.Replace(
                definition,
                @"^\s*" + escapedHeadword + @"\s+",
                string.Empty,
                RegexOptions.IgnoreCase);
        }

        public static string RemoveSeparators(string text, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = text;

            foreach (var separator in separators)
                result = result.Replace(separator.ToString(), string.Empty);

            return result;
        }

        public static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        public static string CleanDefinition(string definition, string? headword = null, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition ?? string.Empty;

            var cleaned = definition;

            bool hasChineseChars = Regex.IsMatch(definition, @"[\u4E00-\u9FFF]");
            bool hasBilingualMarkers =
                definition.Contains('【') || definition.Contains('】') ||
                definition.Contains('•') || definition.Contains('⬄');

            if (hasChineseChars || hasBilingualMarkers)
            {
                cleaned = Regex.Replace(cleaned, @"<[^>]+>", " ");
                cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
                return cleaned;
            }

            cleaned = RemoveIpaMarkers(cleaned);
            cleaned = RemoveSyllableMarkers(cleaned);
            cleaned = RemovePosMarkers(cleaned);

            if (!string.IsNullOrWhiteSpace(headword))
                cleaned = RemoveHeadwordFromDefinition(cleaned, headword);

            if (separators != null && separators.Length > 0)
                cleaned = RemoveSeparators(cleaned, separators);

            cleaned = NormalizeWhitespace(cleaned);

            return cleaned;
        }

        #endregion

        #region Word Normalization

        public static string NormalizeWord(string? word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            var normalized = word.ToLowerInvariant();

            normalized = NormalizeAllDashCharacters(normalized);

            var formattingChars = new[] { "★", "☆", "●", "○", "▶", "【", "】" };
            foreach (var ch in formattingChars)
                normalized = normalized.Replace(ch, "");

            normalized = RemoveDiacritics(normalized);

            normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}\s\-']", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        public static string NormalizeWordPreservingLanguage(string? word, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            var normalized = word.Trim();

            normalized = NormalizeAllDashCharacters(normalized);

            if (ShouldPreserveNonEnglishText(sourceCode))
            {
                var formattingChars = new[] { "★", "☆", "●", "○", "▶" };
                foreach (var ch in formattingChars)
                    normalized = normalized.Replace(ch, "");

                return Regex.Replace(normalized, @"\s+", " ").Trim();
            }

            return NormalizeWord(normalized);
        }

        private static string NormalizeAllDashCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                switch (c)
                {
                    case '\u002D':
                        result.Append('-');
                        break;

                    case '\u2010':
                    case '\u2011':
                    case '\u2012':
                    case '\u2013':
                    case '\u2014':
                    case '\u2015':
                    case '\u2053':
                    case '\u2E17':
                    case '\u2E1A':
                    case '\u2E3A':
                    case '\u2E3B':
                    case '\uFE58':
                    case '\uFE63':
                    case '\uFF0D':
                        result.Append('-');
                        break;

                    case '\u00AD':
                    case '\u1806':
                        break;

                    case '_':
                        result.Append(' ');
                        break;

                    case '~':
                        result.Append('-');
                        break;

                    default:
                        result.Append(c);
                        break;
                }
            }

            return result.ToString();
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        #endregion

        #region POS Normalization

        public static string NormalizePartOfSpeech(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos))
                return "unk";

            var normalized = pos.Trim().ToLowerInvariant();

            return normalized switch
            {
                "noun" or "n." or "n" => "noun",
                "verb" or "v." or "v" or "vi." or "vt." => "verb",
                "adjective" or "adj." or "adj" => "adj",
                "adverb" or "adv." or "adv" => "adv",
                "preposition" or "prep." or "prep" => "preposition",
                "pronoun" or "pron." or "pron" => "pronoun",
                "conjunction" or "conj." or "conj" => "conjunction",
                "interjection" or "interj." or "exclamation" => "exclamation",
                "determiner" or "det." => "determiner",
                "numeral" => "numeral",
                "article" => "determiner",
                "particle" => "particle",
                "phrase" => "phrase",
                "prefix" or "pref." => "prefix",
                "suffix" or "suf." => "suffix",
                "abbreviation" or "abbr." => "abbreviation",
                "symbol" => "symbol",
                _ => normalized.EndsWith('.') ? normalized[..^1] : normalized
            };
        }

        #endregion


        public static string NormalizeSynonymText(string? synonymText)
        {
            if (string.IsNullOrWhiteSpace(synonymText))
                return string.Empty;

            var t = synonymText.Trim();

            // Never store placeholders
            if (t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            t = Regex.Replace(t, @"\s+", " ").Trim();

            t = t.Trim('\"', '\'', '“', '”', '‘', '’', '.', ',', ';', ':', '!', '?');

            if (t.Length > 80)
                t = t.Substring(0, 80).Trim();

            // Keep original behavior: only accept English synonyms
            if (!Regex.IsMatch(t, @"[A-Za-z]"))
                return string.Empty;

            return t;
        }

        private static readonly Regex Noise =
            new(@"[^\p{L}\s]", RegexOptions.Compiled);

        public static string LanguageDetect(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "en";

            foreach (var c in text)
                if (c >= '\u4E00' && c <= '\u9FFF')
                    return "zh";

            return "en";
        }
        public static string NormalizedWordSanitize(string input, string language)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            if (language == "zh")
                return input.Trim();

            var text = input.ToLowerInvariant();
            text = Noise.Replace(text, " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        public static bool IsCanonicalEligible(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (normalized.Contains(' '))
                return false;

            if (normalized.Contains('\''))
                return false;

            return true;
        }

        // NEW METHOD (added)  --  SqlCanonicalWordPronunciationWriter
        public static string NormalizeLocaleCode(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
                return string.Empty;

            var t = localeCode.Trim();

            // keep it simple and stable
            t = t.Replace('_', '-');

            if (t.Length > 15)
                t = t.Substring(0, 15);

            return t;
        }

        // NEW METHOD (added)  --  SqlCanonicalWordPronunciationWriter
        public static string NormalizeIpa(string? ipa)
        {
            if (string.IsNullOrWhiteSpace(ipa))
                return string.Empty;

            var t = ipa.Trim();

            // remove wiki/template remnants if any
            t = t.Replace("[[", "").Replace("]]", "");
            t = t.Replace("{{", "").Replace("}}", "");

            // collapse whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            // hard safety cap
            if (t.Length > 300)
                t = t.Substring(0, 300).Trim();

            // must contain at least something meaningful (IPA symbols are not only A-Z)
            if (t.Length < 2)
                return string.Empty;

            return t;
        }

        public static string IpaNormalize(string ipa)
        {
            if (string.IsNullOrWhiteSpace(ipa))
                return ipa;

            ipa = ipa.Normalize(NormalizationForm.FormC);

            var sb = new StringBuilder(ipa.Length);

            foreach (var ch in ipa)
                switch (ch)
                {
                    case ':':
                        sb.Append('ː');
                        break;

                    case '͡':
                        sb.Append('͜');
                        break;

                    case '.':
                    case ',':
                    case '，':
                        break;

                    case ' ':
                    case '\t':
                    case '\n':
                        sb.Append(' ');
                        break;

                    default:
                        sb.Append(ch);
                        break;
                }

            return sb
                .ToString()
                .Trim()
                .Replace("  ", " ");
        }



        public static class CjkPunctuationStripper
        {
            private static readonly Regex CjkPunctuationRegex =
                new(@"[，。、；：！？【】（）《》〈〉「」『』]",
                    RegexOptions.Compiled);

            public static string RemoveCjkPunctuation(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return input;

                return CjkPunctuationRegex.Replace(input, string.Empty).Trim();
            }
        }
        public static class CjkStripper
        {
            private static readonly Regex CjkRegex =
                new(@"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF]",
                    RegexOptions.Compiled);

            public static string RemoveCjk(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return input;

                return CjkRegex.Replace(input, string.Empty).Trim();
            }
        }
        internal static class DomainMarkerStripper
        {
            private static readonly Regex Marker =
                new(@"^[\(\[【].+?[\)\]】]\s*", RegexOptions.Compiled);

            public static string Strip(string word)
            {
                if (string.IsNullOrWhiteSpace(word))
                    return word;

                return Marker.Replace(word, "").Trim();
            }
        }
        internal static class GenericIpaExtractor
        {
            private static readonly Regex SlashBlockRegex =
                new(@"/([^/]+)/", RegexOptions.Compiled);

            private static readonly Regex IpaPresenceRegex =
                new(@"[ˈˌɑ-ʊəɐɛɪɔʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃ː]",
                    RegexOptions.Compiled);

            private static readonly Regex IpaAllowedCharsRegex =
                new(@"[^ˈˌɑ-ʊəɐɛɪɔʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃ː\. ]",
                    RegexOptions.Compiled);

            private static readonly Regex RejectRegex =
                new(@"^[0-9\s./:-]+$", RegexOptions.Compiled);

            private static readonly Regex ProseRegex =
                new(@"\b(strong|weak|form|plural|singular)\b",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);

            private static readonly Regex EditorialPunctuationRegex =
                new(@"[.,，]", RegexOptions.Compiled);

            private static readonly Regex ParenthesesRegex =
                new(@"[\(\)]", RegexOptions.Compiled);

            private static readonly Regex EdgeHyphenRegex =
                new(@"(^-)|(-$)", RegexOptions.Compiled);

            /// <summary>
            ///     Extracts DISTINCT IPA → Locale mappings.
            ///     IPA is ALWAYS returned in canonical /.../ format.
            /// </summary>
            public static IReadOnlyDictionary<string, string> ExtractIpaWithLocale(string? text)
            {
                var result = new Dictionary<string, string>();

                if (string.IsNullOrWhiteSpace(text))
                    return result;

                var slashMatches = SlashBlockRegex.Matches(text);

                var candidates =
                    slashMatches.Count > 0
                        ? slashMatches.Select(m => m.Groups[1].Value)
                        : [text];

                foreach (var raw in candidates)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    if (RejectRegex.IsMatch(raw))
                        continue;

                    if (ProseRegex.IsMatch(raw))
                        continue;

                    if (!IpaPresenceRegex.IsMatch(raw))
                        continue;

                    var cleaned = raw;

                    cleaned = cleaned.Replace(":", "ː");
                    cleaned = EditorialPunctuationRegex.Replace(cleaned, "");
                    cleaned = ParenthesesRegex.Replace(cleaned, "");
                    cleaned = IpaAllowedCharsRegex.Replace(cleaned, "");
                    cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

                    if (cleaned.Length == 0)
                        continue;

                    var parts =
                        cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var part in parts)
                    {
                        var ipaCore =
                            EdgeHyphenRegex.Replace(part.Trim(), "");

                        if (ipaCore.Length == 0)
                            continue;

                        if (!IpaPresenceRegex.IsMatch(ipaCore))
                            continue;

                        var canonicalIpa = IpaAutoStressNormalizer.Normalize($"/{ipaCore}/");

                        if (result.ContainsKey(canonicalIpa))
                            continue;

                        var detectedLocale =
                            IpaLocaleDetector.Detect(ipaCore);

                        var systemLocale =
                            IpaLocaleDetector.MapToSystemLocale(detectedLocale);

                        if (!string.IsNullOrWhiteSpace(systemLocale)) result.Add(canonicalIpa, systemLocale);
                    }
                }

                return result;
            }

            /// <summary>
            ///     Removes all slash-enclosed IPA blocks from text.
            /// </summary>
            public static string RemoveAll(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return text;

                return SlashBlockRegex.Replace(text, "").Trim();
            }
        }
        internal static class IpaAutoStressNormalizer
        {
            private static readonly Regex StressRegex =
                new(@"[ˈˌ]", RegexOptions.Compiled);

            private static readonly Regex VowelRegex =
                new(@"[ɑæɐəɛɪiɔʊuʌeɜ]", RegexOptions.Compiled);

            /// <summary>
            ///     Injects primary stress (ˈ) if IPA has multiple syllables
            ///     and no existing stress markers.
            /// </summary>
            public static string Normalize(string ipaWithSlashes)
            {
                if (string.IsNullOrWhiteSpace(ipaWithSlashes))
                    return ipaWithSlashes;

                var core = ipaWithSlashes.Trim('/');

                if (StressRegex.IsMatch(core))
                    return ipaWithSlashes;

                var vowelCount = VowelRegex.Matches(core).Count;
                if (vowelCount < 2)
                    return ipaWithSlashes;

                var stressed = "ˈ" + core;

                return $"/{stressed}/";
            }
        }
        internal static class IpaLocaleDetector
        {
            private static readonly Regex AmericanMarkers =
                new(@"[ɹɑɚɝoʊ]", RegexOptions.Compiled);

            private static readonly Regex BritishMarkers =
                new(@"[ɒəʊː]", RegexOptions.Compiled);

            /// <summary>
            ///     Detects IPA locale using phonetic markers.
            ///     Returns a BCP-47 language tag.
            /// </summary>
            public static string Detect(string ipa)
            {
                if (string.IsNullOrWhiteSpace(ipa))
                    return "en";

                var usScore = 0;
                var gbScore = 0;

                if (AmericanMarkers.IsMatch(ipa))
                    usScore++;

                if (BritishMarkers.IsMatch(ipa))
                    gbScore++;

                if (ipa.Contains("ɚ") || ipa.Contains("ɝ"))
                    usScore += 2;

                if (ipa.Contains("ː"))
                    gbScore += 2;

                if (usScore > gbScore)
                    return "en-US";

                if (gbScore > usScore)
                    return "en-GB";

                return "en";
            }

            /// <summary>
            ///     Optional compatibility mapping for systems using en-UK.
            /// </summary>
            public static string MapToSystemLocale(string detectedLocale)
            {
                return detectedLocale switch
                {
                    "en-GB" => "en-UK",
                    _ => detectedLocale
                };
            }
        }

        internal static class IpaSyllablePostProcessor
        {
            private static readonly Regex VowelRegex =
                new(@"[aeiouæɪʊəɐɑɔɛɜʌoøɒyɯɨɶ]", RegexOptions.Compiled);

            private static readonly Regex ConsonantRegex =
                new(@"[bcdfghjklmnpqrstvwxyzθðʃʒŋ]", RegexOptions.Compiled);

            /// <summary>
            ///     Normalizes syllables by merging invalid syllables
            ///     into their previous neighbor.
            /// </summary>
            public static IReadOnlyList<IpaSyllable> Normalize(
                IReadOnlyList<IpaSyllable> syllables)
            {
                if (syllables == null || syllables.Count == 0)
                    return syllables;

                var buffer = new List<IpaSyllable>();

                foreach (var current in syllables)
                {
                    if (buffer.Count == 0)
                    {
                        buffer.Add(current);
                        continue;
                    }

                    var hasVowel = VowelRegex.IsMatch(current.Text);
                    var hasConsonant = ConsonantRegex.IsMatch(current.Text);

                    if (!hasVowel || !hasConsonant)
                    {
                        var prev = buffer[^1];

                        buffer[^1] = new IpaSyllable(
                            prev.Index,
                            prev.Text + current.Text,
                            Math.Max(prev.StressLevel, current.StressLevel));
                    }
                    else
                    {
                        buffer.Add(current);
                    }
                }

                var result = new List<IpaSyllable>(buffer.Count);
                var index = 1;

                foreach (var s in buffer)
                    result.Add(
                        new IpaSyllable(
                            index++,
                            s.Text,
                            s.StressLevel));

                return result;
            }
        }

        internal static class IpaSyllabifier
        {
            private static readonly HashSet<char> Vowels =
            [
                'i', 'y', 'ɪ', 'ʏ', 'e', 'ø', 'ɛ', 'œ', 'æ', 'a', 'ɑ', 'ɒ', 'ɔ', 'o', 'ʊ', 'u',
                'ə', 'ɚ', 'ɝ', 'ɜ', 'ɵ', 'ɐ'
            ];

            public static IReadOnlyList<IpaSyllable> Split(string ipa)
            {
                var result = new List<IpaSyllable>();

                if (string.IsNullOrWhiteSpace(ipa))
                    return result;

                var buffer = new StringBuilder();
                var hasVowel = false;
                byte currentStress = 0;
                var index = 1;

                foreach (var ch in ipa)
                {
                    if (ch == 'ˈ')
                    {
                        currentStress = 2;
                        continue;
                    }

                    if (ch == 'ˌ')
                    {
                        currentStress = 1;
                        continue;
                    }

                    buffer.Append(ch);

                    if (Vowels.Contains(ch))
                    {
                        if (hasVowel)
                        {
                            result.Add(
                                new IpaSyllable(
                                    index++,
                                    buffer.ToString(0, buffer.Length - 1),
                                    currentStress));

                            buffer.Clear();
                            buffer.Append(ch);
                            currentStress = 0;
                        }

                        hasVowel = true;
                    }
                }

                if (buffer.Length > 0)
                    result.Add(
                        new IpaSyllable(
                            index,
                            buffer.ToString(),
                            currentStress));

                return result;
            }
        }

        public static class LanguageDetector
        {
            private static readonly ILanguageDetectionService _service = new LanguageDetectionService();

            public static bool ContainsNonEnglishText(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                return _service.ContainsNonEnglish(text);
            }

            public static string? DetectLanguageCode(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return _service.DetectPrimaryLanguage(text);
            }

            public static bool IsBilingualText(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                return _service.IsBilingualText(text);
            }
        }
        internal static class IpaStressRenderer
        {
            public static string Render(
                IReadOnlyList<IpaSyllable> syllables,
                IpaStressRenderProfile profile)
            {
                if (syllables == null || syllables.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();

                var useDots = profile == IpaStressRenderProfile.EnUk;

                sb.Append('/');

                for (var i = 0; i < syllables.Count; i++)
                {
                    var s = syllables[i];

                    if (i > 0 && useDots)
                        sb.Append('.');

                    if (s.StressLevel == 2)
                        sb.Append('ˈ');
                    else if (s.StressLevel == 1)
                        sb.Append('ˌ');

                    sb.Append(s.Text);
                }

                sb.Append('/');

                return sb.ToString();
            }
        }

        public static class OrthographicSyllableExtractor
        {
            private static readonly Regex VowelRegex =
                new(@"[aeiouyAEIOUY]", RegexOptions.Compiled);

            /// <summary>
            ///     Extracts orthographic syllables from a word.
            /// </summary>
            public static IReadOnlyList<string> Extract(string word)
            {
                var result = new List<string>();

                if (string.IsNullOrWhiteSpace(word))
                    return result;

                word = word.Trim();

                var last = 0;

                for (var i = 1; i < word.Length - 1; i++)
                    if (VowelRegex.IsMatch(word[i - 1].ToString()) &&
                        !VowelRegex.IsMatch(word[i].ToString()) &&
                        VowelRegex.IsMatch(word[i + 1].ToString()))
                    {
                        result.Add(word.Substring(last, i - last));
                        last = i;
                    }

                result.Add(word.Substring(last));
                return result;
            }
        }
        public static class OrthographicSyllableRenderer
        {
            public static string Render(
                IReadOnlyList<string> syllables)
            {
                return syllables == null || syllables.Count == 0
                    ? string.Empty
                    : string.Join("·", syllables);
            }
        }
    }
}
