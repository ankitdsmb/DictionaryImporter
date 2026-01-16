namespace DictionaryImporter.AITextKit.Grammar;

public sealed class LanguageDetector : ILanguageDetector
{
    private readonly Dictionary<string, LanguageProfile> _languageProfiles;

    public LanguageDetector()
    {
        Console.WriteLine("Initializing LanguageDetector...");

        _languageProfiles = new Dictionary<string, LanguageProfile>
        {
            ["en-US"] = new LanguageProfile("English", new[]
            {
                "the", "and", "ing", "of", "to", "in", "a", "that", "is", "was",
                "for", "it", "with", "as", "his", "he", "be", "by", "on", "not"
            }),
            ["fr"] = new LanguageProfile("French", new[]
            {
                "de", "la", "le", "et", "les", "des", "que", "un", "une", "est",
                "pour", "dans", "par", "qui", "il", "elle", "nous", "vous", "ils"
            }),
            ["de"] = new LanguageProfile("German", new[]
            {
                "und", "der", "die", "den", "das", "von", "mit", "sich", "des", "ist",
                "dem", "ein", "eine", "auf", "für", "im", "nicht", "auch", "es", "an"
            }),
            ["es"] = new LanguageProfile("Spanish", new[]
            {
                "de", "la", "que", "el", "en", "y", "los", "se", "del", "las",
                "un", "por", "con", "no", "una", "su", "al", "lo", "como", "más"
            }),
            ["it"] = new LanguageProfile("Italian", new[]
            {
                "di", "e", "che", "la", "il", "a", "in", "per", "con", "non",
                "del", "una", "da", "sono", "è", "un", "si", "lo", "gli", "le"
            })
        };

        Console.WriteLine($"Initialized with {_languageProfiles.Count} languages");
    }

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en-US";

        if (text.Length < 10)
            return "en-US"; // Too short for reliable detection

        try
        {
            text = text.ToLowerInvariant();

            var scores = new Dictionary<string, double>();

            foreach (var lang in _languageProfiles.Keys)
            {
                double score = 0;
                var profile = _languageProfiles[lang];

                foreach (var word in profile.CommonWords)
                {
                    // Count word occurrences (as whole words)
                    var pattern = $@"\b{Regex.Escape(word)}\b";
                    var count = Regex.Matches(text, pattern).Count;
                    score += count * 1.0;

                    // Also check for partial matches
                    if (text.Contains(word))
                    {
                        score += 0.5;
                    }
                }

                // Normalize by text length
                score = score / (text.Length / 100.0);
                scores[lang] = score;
            }

            // Get best match
            var bestMatch = scores.OrderByDescending(kv => kv.Value).FirstOrDefault();

            // Debug output
            if (scores.Values.Any(v => v > 0))
            {
                var topScores = scores
                    .Where(kv => kv.Value > 0)
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .Select(kv => $"{_languageProfiles[kv.Key].Name}:{kv.Value:F2}");

                Console.WriteLine($"Language scores: {string.Join(", ", topScores)}");
            }

            // Return best match if score is reasonable, otherwise default to English
            return bestMatch.Value > 1.0 ? bestMatch.Key : "en-US";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error detecting language: {ex.Message}");
            return "en-US";
        }
    }

    private class LanguageProfile
    {
        public string Name { get; }
        public string[] CommonWords { get; }

        public LanguageProfile(string name, string[] commonWords)
        {
            Name = name;
            CommonWords = commonWords;
        }
    }
}