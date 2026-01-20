using System.Globalization;

namespace DictionaryImporter.Sources.Common.Helper
{
    public static class TextNormalizer
    {
        public static string NormalizeWord(string? word)
        {
            if (string.IsNullOrWhiteSpace(word)) return string.Empty;
            var normalized = word.ToLowerInvariant();
            // FIX: Normalize ALL dash/hyphen characters to regular hyphen U+002D
            normalized = NormalizeAllDashCharacters(normalized);
            // Remove formatting characters
            var formattingChars = new[] { "★", "☆", "●", "○", "▶", "【", "】" };
            foreach (var ch in formattingChars)
            {
                normalized = normalized.Replace(ch, "");
            }
            // Remove diacritics
            normalized = RemoveDiacritics(normalized);
            // Remove non-letter/number characters (except hyphen and apostrophe)
            // After normalization, all dashes are U+002D, so they'll be preserved
            normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}\s\-']", " ");
            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private static string NormalizeAllDashCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                // Map ALL dash/hyphen-like characters appropriately
                switch (c)
                {
                    // Regular hyphen - keep as hyphen
                    case '\u002D': // HYPHEN-MINUS
                        result.Append('-');
                        break;
                    // Dash characters that should become hyphen
                    case '\u2010': // HYPHEN
                    case '\u2011': // NON-BREAKING HYPHEN
                    case '\u2012': // FIGURE DASH
                    case '\u2013': // EN DASH
                    case '\u2014': // EM DASH
                    case '\u2015': // HORIZONTAL BAR
                    case '\u2053': // SWUNG DASH
                    case '\u2E17': // DOUBLE OBLIQUE HYPHEN
                    case '\u2E1A': // HYPHEN WITH DIAERESIS
                    case '\u2E3A': // TWO-EM DASH
                    case '\u2E3B': // THREE-EM DASH
                    case '\uFE58': // SMALL EM DASH
                    case '\uFE63': // SMALL HYPHEN-MINUS
                    case '\uFF0D': // FULLWIDTH HYPHEN-MINUS
                        result.Append('-'); // U+002D
                        break;
                    // SOFT HYPHEN and similar - should be REMOVED (not space)
                    case '\u00AD': // SOFT HYPHEN
                    case '\u1806': // MONGOLIAN TODO SOFT HYPHEN
                        // Don't append anything - remove it completely
                        break;
                    // Underscore - treat as space
                    case '_':
                        result.Append(' ');
                        break;
                    // Tilde sometimes used as dash substitute
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

        // Ensure RemoveDiacritics handles all cases
        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();
            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        // FIXED: This was breaking bilingual sources by removing Chinese characters
        public static string NormalizeWordPreservingLanguage(string? word, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(word)) return string.Empty;

            var normalized = word.ToLowerInvariant().Trim();
            // Apply dash normalization for ALL sources
            normalized = NormalizeAllDashCharacters(normalized);

            // FIX: For bilingual sources, ONLY remove formatting characters, do NOT call NormalizeWord()
            var bilingualSources = new HashSet<string>
            {
                "ENG_CHN", "CENTURY21", "ENG_OXFORD", "ENG_COLLINS"
            };

            if (bilingualSources.Contains(sourceCode))
            {
                // Only remove specific formatting characters
                var formattingChars = new[] { "★", "☆", "●", "○", "▶" };
                foreach (var ch in formattingChars)
                {
                    normalized = normalized.Replace(ch, "");
                }
                return normalized.Trim();
            }

            // For Latin-based sources, use full normalization
            return NormalizeWord(normalized);
        }

        public static string NormalizePartOfSpeech(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos)) return "unk";
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
    }
}