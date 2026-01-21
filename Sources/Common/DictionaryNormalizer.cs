public static class DictionaryNormalizer
{
    private static readonly Regex RatingSymbolRegex =
        new Regex(@"[●○◇□■▲►▼◄◊◦¤∙•▪▫◘◙☺☻♀♂♠♣♥♦♪♫♯]", RegexOptions.Compiled);

    private static readonly HashSet<string> AbbreviationWords = new HashSet<string>
    {
        "3G", "4to", "8vo", "4WD", "A3", "AAAS", "AAD", "AAM"
    };

    public static NormalizedWordResult NormalizeWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return new NormalizedWordResult { Original = word, Normalized = word };

        // Remove rating symbols
        var cleanedWord = RatingSymbolRegex.Replace(word, "").Trim();

        // Special handling for known abbreviations
        if (AbbreviationWords.Contains(word.ToUpperInvariant()))
        {
            return new NormalizedWordResult
            {
                Original = word,
                Normalized = word.ToLowerInvariant(),
                IsAbbreviation = true
            };
        }

        // Standard normalization
        var normalized = cleanedWord.ToLowerInvariant();

        // Remove trailing punctuation but keep internal hyphens
        normalized = Regex.Replace(normalized, @"[^\w\-]+$", "");

        return new NormalizedWordResult
        {
            Original = word,
            Normalized = normalized,
            IsAbbreviation = false
        };
    }

    public static (string Word, string NormalizedWord) CleanAndNormalize(string rawWord)
    {
        var result = NormalizeWord(rawWord);
        return (result.Original, result.Normalized);
    }

    public class NormalizedWordResult
    {
        public string Original { get; set; }
        public string Normalized { get; set; }
        public bool IsAbbreviation { get; set; }
    }
}