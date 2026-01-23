using System.Globalization;
using System.Net;

namespace DictionaryImporter.Sources.Common.Helper
{
    public static class Helper
    {
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

        private const int MAX_RECORDS_PER_SOURCE = 500;

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
    }
}
