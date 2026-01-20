namespace DictionaryImporter.Sources.Oxford.Extractor
{
    public sealed class OxfordSynonymExtractor(ILogger<OxfordSynonymExtractor> logger) : ISynonymExtractor
    {
        private static readonly Regex[] HighConfidencePatterns =
        [
            new(@"^\s*(?<word1>\w+)\s+means\s+(?<word2>\w+)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"^\s*(?<word1>\w+)\s+is\s+(?<word2>\w+)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"^\s*(?<word1>\w+)\s+is\s+the\s+same\s+as\s+(?<word2>\w+)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)
        ];

        private static readonly Regex[] MediumConfidencePatterns =
        [
            new(@"^\s*(?<word1>\w+)\s*,\s*or\s+(?<word2>\w+)\s*,",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"^\s*(?<word1>\w+)\s*\(\s*also\s+(?<word2>\w+)\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)
        ];

        private static readonly Regex[] DefinitionReplacementPatterns =
        [
            new(@"^If you (?<word1>\w+) [^,]+, you (?<word2>\w+) (?:it|something|one)\.?$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"^To (?<word1>\w+) means to (?<word2>\w+)(?:\.|$)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled)
        ];

        private readonly ILogger<OxfordSynonymExtractor> _logger = logger;

        public string SourceCode => "ENG_OXFORD";

        public IReadOnlyList<SynonymDetectionResult> Extract(
            string headword,
            string definition,
            string? rawDefinition = null)
        {
            var results = new List<SynonymDetectionResult>();

            if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(headword))
                return results;

            var cleanedHeadword = CleanHeadword(headword);
            var cleanedDefinition = PreprocessDefinition(definition);

            foreach (var pattern in HighConfidencePatterns)
            {
                var match = pattern.Match(cleanedDefinition);
                if (match.Success)
                {
                    var word1 = match.Groups["word1"].Value.ToLowerInvariant();
                    var word2 = match.Groups["word2"].Value.ToLowerInvariant();

                    if (IsValidSynonymPair(cleanedHeadword, word1, word2, out var targetWord))
                        results.Add(new SynonymDetectionResult
                        {
                            TargetHeadword = targetWord!,
                            ConfidenceLevel = "high",
                            DetectionMethod = $"Pattern: {pattern}",
                            SourceText = match.Value
                        });
                }
            }

            foreach (var pattern in DefinitionReplacementPatterns)
            {
                var match = pattern.Match(cleanedDefinition);
                if (match.Success)
                {
                    var word1 = match.Groups["word1"].Value.ToLowerInvariant();
                    var word2 = match.Groups["word2"].Value.ToLowerInvariant();

                    if (IsValidSynonymPair(cleanedHeadword, word1, word2, out var targetWord))
                        results.Add(new SynonymDetectionResult
                        {
                            TargetHeadword = targetWord!,
                            ConfidenceLevel = "high",
                            DetectionMethod = $"DefinitionReplacement: {pattern}",
                            SourceText = match.Value
                        });
                }
            }

            foreach (var pattern in MediumConfidencePatterns)
            {
                var match = pattern.Match(cleanedDefinition);
                if (match.Success)
                {
                    var word1 = match.Groups["word1"].Value.ToLowerInvariant();
                    var word2 = match.Groups["word2"].Value.ToLowerInvariant();

                    if (IsValidSynonymPair(cleanedHeadword, word1, word2, out var targetWord))
                        results.Add(new SynonymDetectionResult
                        {
                            TargetHeadword = targetWord!,
                            ConfidenceLevel = "medium",
                            DetectionMethod = $"Contextual: {pattern}",
                            SourceText = match.Value
                        });
                }
            }

            var deduplicated = results
                .GroupBy(r => r.TargetHeadword)
                .Select(g => g.OrderByDescending(r => GetConfidenceScore(r.ConfidenceLevel)).First())
                .ToList();

            return deduplicated;
        }

        public bool ValidateSynonymPair(string headwordA, string headwordB)
        {
            if (string.IsNullOrWhiteSpace(headwordA) || string.IsNullOrWhiteSpace(headwordB))
                return false;

            var a = CleanHeadword(headwordA);
            var b = CleanHeadword(headwordB);

            if (a == b)
                return false;

            if (!IsValidHeadword(a) || !IsValidHeadword(b))
                return false;

            if (a.Length < 2 || b.Length < 2)
                return false;

            return true;
        }

        private string CleanHeadword(string headword)
        {
            if (string.IsNullOrWhiteSpace(headword))
                return string.Empty;

            return headword
                .ToLowerInvariant()
                .Replace("★", "")
                .Replace("☆", "")
                .Replace("●", "")
                .Replace("○", "")
                .Replace("▶", "")
                .Trim();
        }

        private string PreprocessDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var cleaned = definition
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("  ", " ");

            cleaned = Regex.Replace(cleaned, @"【[^】]+】", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        private bool IsValidSynonymPair(string currentHeadword, string word1, string word2, out string? targetWord)
        {
            targetWord = null;

            if (!IsValidHeadword(word1) || !IsValidHeadword(word2))
                return false;

            if (word1 == currentHeadword)
                targetWord = word2;
            else if (word2 == currentHeadword)
                targetWord = word1;
            else
                return false;

            return ValidateSynonymPair(currentHeadword, targetWord);
        }

        private bool IsValidHeadword(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                return false;

            if (!word.Any(char.IsLetter))
                return false;

            if (!Regex.IsMatch(word, @"^[a-z\-']+$"))
                return false;

            return true;
        }

        private int GetConfidenceScore(string confidence)
        {
            return confidence switch
            {
                "high" => 100,
                "medium" => 60,
                "low" => 30,
                _ => 0
            };
        }
    }
}