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

            if (string.IsNullOrWhiteSpace(headword))
                return results;

            try
            {
                // Try to extract from rawDefinition first (JSON)
                if (!string.IsNullOrWhiteSpace(rawDefinition) &&
                    rawDefinition.StartsWith("{") && rawDefinition.EndsWith("}"))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawDefinition, options);

                    if (rawData != null && rawData.TryGetValue("synonyms", out var synonymsElement))
                    {
                        foreach (var synonym in synonymsElement.EnumerateArray())
                        {
                            if (synonym.TryGetProperty("word", out var wordProp) &&
                                wordProp.ValueKind == JsonValueKind.String)
                            {
                                var synonymWord = wordProp.GetString();
                                if (!string.IsNullOrWhiteSpace(synonymWord) &&
                                    ValidateSynonymPair(headword, synonymWord))
                                {
                                    results.Add(new SynonymDetectionResult
                                    {
                                        TargetHeadword = synonymWord.ToLowerInvariant(),
                                        ConfidenceLevel = "high",
                                        DetectionMethod = "KaikkiExplicitSynonym",
                                        SourceText = $"Explicit synonym: {synonymWord}"
                                    });
                                }
                            }
                        }
                    }
                }

                // Fallback: extract from formatted definition
                if (results.Count == 0 && !string.IsNullOrWhiteSpace(definition))
                {
                    var synonyms = ExtractSynonymsFromDefinition(definition);
                    foreach (var synonym in synonyms)
                    {
                        if (ValidateSynonymPair(headword, synonym))
                        {
                            results.Add(new SynonymDetectionResult
                            {
                                TargetHeadword = synonym.ToLowerInvariant(),
                                ConfidenceLevel = "medium",
                                DetectionMethod = "KaikkiFormattedSynonym",
                                SourceText = $"Synonym in definition: {synonym}"
                            });
                        }
                    }
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
            return !nonSynonyms.Any(p => (p.Item1 == a && p.Item2 == b) || (p.Item1 == b && p.Item2 == a));
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