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

            if (string.IsNullOrWhiteSpace(definition))
                return results;

            try
            {
                // Extract synonyms from Kaikki's formatted definition
                // Kaikki definitions have synonyms in 【Synonyms】section
                var synonyms = ExtractSynonymsFromDefinition(definition);

                foreach (var synonym in synonyms)
                {
                    if (string.IsNullOrWhiteSpace(synonym) || synonym == headword)
                        continue;

                    if (!IsValidHeadword(synonym))
                        continue;

                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = synonym.Trim().ToLowerInvariant(),
                        ConfidenceLevel = "high", // Kaikki synonyms are explicit
                        DetectionMethod = "KaikkiExplicitSynonym",
                        SourceText = $"Explicit synonym: {synonym}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract synonyms for {Headword}", headword);
            }

            return results;
        }

        private List<string> ExtractSynonymsFromDefinition(string definition)
        {
            var synonyms = new List<string>();

            if (!definition.Contains("【Synonyms】"))
                return synonyms;

            var lines = definition.Split('\n');
            var inSynonymsSection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("【Synonyms】"))
                {
                    inSynonymsSection = true;
                    continue;
                }

                if (inSynonymsSection)
                {
                    if (trimmedLine.StartsWith("【")) // New section started
                        break;

                    if (trimmedLine.StartsWith("• "))
                    {
                        var synonym = trimmedLine.Substring(2).Trim();

                        // Remove any parenthetical sense info
                        var parenIndex = synonym.IndexOf('(');
                        if (parenIndex > 0)
                            synonym = synonym.Substring(0, parenIndex).Trim();

                        if (!string.IsNullOrWhiteSpace(synonym))
                            synonyms.Add(synonym);
                    }
                }
            }

            return synonyms;
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

            // Check for obvious non-synonyms
            var nonSynonyms = new[] { ("big", "small"), ("hot", "cold"), ("up", "down") };
            return !nonSynonyms.Any(p => p.Item1 == a && p.Item2 == b || p.Item1 == b && p.Item2 == a);
        }

        private bool IsValidHeadword(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                return false;

            // Must contain at least one letter
            if (!word.Any(char.IsLetter))
                return false;

            // Allow letters, hyphens, and apostrophes
            return Regex.IsMatch(word, @"^[a-z\-']+$");
        }
    }
}