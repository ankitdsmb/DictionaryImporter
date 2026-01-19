namespace DictionaryImporter.Sources.Common.Helper
{
    public static class TextNormalizer
    {
        private static readonly string[] SpecialCharacters = { "★", "☆", "●", "○", "▶" };

        public static string NormalizeWord(string? word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            var normalized = word.ToLowerInvariant();

            normalized = SpecialCharacters.Aggregate(normalized, (current, specialChar) => current.Replace(specialChar, ""));

            normalized = normalized
                .Replace('\u2013', '-')
                .Replace('\u2014', '-')
                .Replace('\u2011', '-')
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();

            // Remove any remaining special characters
            normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}\s\-']", " ");

            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        public static string? NormalizeDefinition(string? definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            var normalized = definition.Trim();

            // Remove HTML tags
            normalized = Regex.Replace(normalized, @"<[^>]+>", " ");

            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        public static bool IsDefinitionValid(string? definition, int minLength = 5)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return false;

            // Check minimum length (excluding whitespace)
            var trimmed = definition.Trim();
            if (trimmed.Length < minLength)
                return false;

            // Check if it's just a single character or symbol
            if (trimmed.Length == 1 && !char.IsLetterOrDigit(trimmed[0]))
                return false;

            // Check if it contains at least some letters
            if (!trimmed.Any(char.IsLetter))
                return false;

            return true;
        }

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

        public static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var s = input.ToLowerInvariant();

            s = s.Replace("(", "")
                .Replace(")", "")
                .Replace(",", " ");

            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }
    }
}