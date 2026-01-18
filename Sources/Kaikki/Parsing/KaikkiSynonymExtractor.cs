using DictionaryImporter.Sources.Kaikki.Helpers;

namespace DictionaryImporter.Infrastructure.Parsing.SynonymExtractor
{
    internal class KaikkiSynonymExtractor : ISynonymExtractor
    {
        private readonly ILogger<KaikkiSynonymExtractor> _logger;

        public KaikkiSynonymExtractor(ILogger<KaikkiSynonymExtractor> logger)
        {
            _logger = logger;
        }

        public string SourceCode => "KAIKKI";

        public IReadOnlyList<SynonymDetectionResult> Extract(
            string headword,
            string definition,
            string? rawDefinition = null)
        {
            var results = new List<SynonymDetectionResult>();

            try
            {
                // Only process English Kaikki entries
                if (string.IsNullOrWhiteSpace(rawDefinition) ||
                    !KaikkiJsonHelper.IsEnglishEntry(rawDefinition))
                {
                    return results;
                }

                var synonyms = KaikkiJsonHelper.ExtractSynonyms(rawDefinition);

                foreach (var synonym in synonyms)
                {
                    if (ValidateSynonymPair(headword, synonym))
                    {
                        results.Add(new SynonymDetectionResult
                        {
                            TargetHeadword = synonym.ToLowerInvariant(),
                            ConfidenceLevel = "high",
                            DetectionMethod = "KaikkiStructuredSynonym",
                            SourceText = $"Kaikki synonym: {synonym}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract synonyms for {Headword}", headword);
            }

            return results;
        }

        public bool ValidateSynonymPair(string headwordA, string headwordB)
        {
            if (string.IsNullOrWhiteSpace(headwordA) || string.IsNullOrWhiteSpace(headwordB))
                return false;

            var a = headwordA.ToLowerInvariant().Trim();
            var b = headwordB.ToLowerInvariant().Trim();

            if (a == b)
                return false;

            if (!IsValidHeadword(a) || !IsValidHeadword(b))
                return false;

            if (a.Length < 2 || b.Length < 2)
                return false;

            // Check for obvious non-synonyms (antonyms)
            var antonyms = new[]
            {
                ("big", "small"), ("hot", "cold"), ("up", "down"),
                ("good", "bad"), ("yes", "no"), ("black", "white"),
                ("day", "night"), ("fast", "slow"), ("high", "low"),
                ("love", "hate"), ("rich", "poor"), ("strong", "weak")
            };

            return !antonyms.Any(p =>
                (p.Item1 == a && p.Item2 == b) ||
                (p.Item1 == b && p.Item2 == a));
        }

        private bool IsValidHeadword(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                return false;

            // Must contain at least one letter
            if (!word.Any(char.IsLetter))
                return false;

            // Allow letters, hyphens, apostrophes, and spaces (for phrases)
            return Regex.IsMatch(word, @"^[a-z\s\-']+$");
        }
    }
}