using System.Text.Json;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using JsonException = System.Text.Json.JsonException;

namespace DictionaryImporter.Sources.Kaikki.Parsing
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
                if (string.IsNullOrWhiteSpace(rawDefinition))
                    return results;

                using var doc = JsonDocument.Parse(rawDefinition);
                var root = doc.RootElement;

                if (!JsonProcessor.IsEnglishEntry(root))
                    return results;

                var synonyms = SourceDataHelper.ExtractSynonyms(rawDefinition);

                foreach (var synonym in synonyms)
                {
                    if (!ValidateSynonymPair(headword, synonym))
                        continue;

                    var normalizedTarget = SourceDataHelper.NormalizeWord(synonym);
                    if (string.IsNullOrWhiteSpace(normalizedTarget))
                        continue;

                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = normalizedTarget,
                        ConfidenceLevel = "high",
                        DetectionMethod = "KaikkiStructuredSynonym",
                        SourceText = $"Kaikki synonym: {synonym}"
                    });
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse Kaikki JSON for synonym extraction | Headword={Headword}", headword);
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
                p.Item1 == a && p.Item2 == b ||
                p.Item1 == b && p.Item2 == a);
        }

        private bool IsValidHeadword(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                return false;

            if (!word.Any(char.IsLetter))
                return false;

            // Allow letters, hyphens, apostrophes, and spaces (phrases)
            return Regex.IsMatch(word, @"^[a-z\s\-']+$");
        }
    }
}