using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Collins.Extractor;

internal class CollinsSynonymExtractor(ILogger<CollinsSynonymExtractor> logger) : ISynonymExtractor
{
    private static readonly Regex[] HighConfidencePatterns =
    [
        new(@"^(?<word1>\w+)\s+means\s+(?:the\s+)?same(?:\s+as)?\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^(?<word1>\w+)\s+is\s+(?:a\s+)?synonym\s+of\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^(?<word1>\w+)\s+and\s+(?<word2>\w+)\s+are\s+synonyms\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^the\s+same\s+as\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private static readonly Regex[] MediumConfidencePatterns =
    [
        new(@"\b(?<word1>\w+)\s+(?:or|and\/or)\s+(?<word2>\w+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\balso\s+called\s+(?<word2>\w+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bknown\s+as\s+(?<word2>\w+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private static readonly Regex[] LowConfidencePatterns =
    [
        new(@"\bsynonymous\s+with\s+(?<word2>\w+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bsimilar\s+to\s+(?<word2>\w+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bcompare\s+with\s+(?<word2>\w+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bsee\s+also\s+(?<word2>\w+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private static readonly Regex[] DefinitionReplacementPatterns =
    [
        new(@"^If you (?<word1>\w+)[^\.,]+, you (?<word2>\w+)(?:\s+it|\s+something)?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^To (?<word1>\w+)\s+(?:something\s+)?means to (?<word2>\w+)(?:\s+it)?(?:\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^(?<word1>\w+ing)\s+is\s+(?<word2>\w+ing)(?:\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^When you (?<word1>\w+)[^\.,]+, you (?<word2>\w+)[^\.,]*\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    public string SourceCode => "ENG_COLLINS";

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

        logger.LogDebug("Extracting synonyms for {Headword} | Definition: {Definition}",
            cleanedHeadword, cleanedDefinition.Substring(0, Math.Min(100, cleanedDefinition.Length)));

        // Extract from patterns
        ExtractFromPatterns(cleanedHeadword, cleanedDefinition, results);

        // Check for explicit references in definition
        ExtractFromExplicitReferences(cleanedHeadword, cleanedDefinition, results);

        // Deduplicate
        var deduplicated = results
            .GroupBy(r => r.TargetHeadword.ToLowerInvariant())
            .Select(g => g.OrderByDescending(r => GetConfidenceScore(r.ConfidenceLevel)).First())
            .ToList();

        logger.LogDebug("Found {Count} synonyms for {Headword}: {Synonyms}",
            deduplicated.Count, cleanedHeadword,
            string.Join(", ", deduplicated.Select(r => r.TargetHeadword)));

        return deduplicated;
    }

    private void ExtractFromPatterns(string headword, string definition, List<SynonymDetectionResult> results)
    {
        // High confidence patterns
        foreach (var pattern in HighConfidencePatterns)
        {
            var match = pattern.Match(definition);
            if (match.Success)
            {
                ProcessMatch(match, headword, pattern.ToString(), "high", results);
            }
        }

        // Definition replacement patterns
        foreach (var pattern in DefinitionReplacementPatterns)
        {
            var match = pattern.Match(definition);
            if (match.Success)
            {
                ProcessMatch(match, headword, $"DefinitionReplacement: {pattern}", "high", results);
            }
        }

        // Medium confidence patterns
        foreach (var pattern in MediumConfidencePatterns)
        {
            var matches = pattern.Matches(definition);
            foreach (Match match in matches)
            {
                ProcessMatch(match, headword, $"Contextual: {pattern}", "medium", results);
            }
        }

        // Low confidence patterns
        foreach (var pattern in LowConfidencePatterns)
        {
            var match = pattern.Match(definition);
            if (match.Success)
            {
                var word2 = match.Groups["word2"].Value.ToLowerInvariant();
                if (word2 != headword && IsValidHeadword(word2))
                {
                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = word2,
                        ConfidenceLevel = "low",
                        DetectionMethod = $"Inference: {pattern}",
                        SourceText = match.Value
                    });
                }
            }
        }
    }

    private void ProcessMatch(Match match, string headword, string patternName, string confidence,
        List<SynonymDetectionResult> results)
    {
        var word1Group = match.Groups["word1"];
        var word2Group = match.Groups["word2"];

        if (word1Group.Success && word2Group.Success)
        {
            var word1 = word1Group.Value.ToLowerInvariant();
            var word2 = word2Group.Value.ToLowerInvariant();

            if (IsValidSynonymPair(headword, word1, word2, out var targetWord))
            {
                results.Add(new SynonymDetectionResult
                {
                    TargetHeadword = targetWord!,
                    ConfidenceLevel = confidence,
                    DetectionMethod = patternName,
                    SourceText = match.Value
                });
            }
        }
        else if (word2Group.Success)
        {
            var word2 = word2Group.Value.ToLowerInvariant();
            if (word2 != headword && IsValidHeadword(word2))
            {
                results.Add(new SynonymDetectionResult
                {
                    TargetHeadword = word2,
                    ConfidenceLevel = confidence,
                    DetectionMethod = patternName,
                    SourceText = match.Value
                });
            }
        }
    }

    private void ExtractFromExplicitReferences(string headword, string definition, List<SynonymDetectionResult> results)
    {
        // Special cases like envisage/envision
        if (headword == "envisage" || headword == "envision")
        {
            var otherWord = headword == "envisage" ? "envision" : "envisage";
            if (definition.Contains(otherWord, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new SynonymDetectionResult
                {
                    TargetHeadword = otherWord,
                    ConfidenceLevel = "high",
                    DetectionMethod = "ExplicitDefinitionReference",
                    SourceText = $"Definition references {otherWord}"
                });
            }
        }

        // Check for "also called" patterns
        if (definition.Contains("also called", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(definition, @"also called\s+(?<word>\w+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var target = match.Groups["word"].Value.ToLowerInvariant();
                if (target != headword && IsValidHeadword(target))
                {
                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = target,
                        ConfidenceLevel = "medium",
                        DetectionMethod = "AlsoCalled",
                        SourceText = match.Value
                    });
                }
            }
        }
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

        if (IsObviousNonSynonymPair(a, b))
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
            .Trim();
    }

    private string PreprocessDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var cleaned = definition
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Replace("  ", " ");

        // Remove Collins-specific markers
        cleaned = Regex.Replace(cleaned, @"●+○+\s*", " ");
        cleaned = Regex.Replace(cleaned, @"★+☆+\s*", " ");

        // Normalize whitespace
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

        // Allow letters, hyphens, and apostrophes
        if (!Regex.IsMatch(word, @"^[a-z\-']+$"))
            return false;

        return true;
    }

    private bool IsObviousNonSynonymPair(string a, string b)
    {
        var nonSynonyms = new[]
        {
            ("big", "small"),
            ("hot", "cold"),
            ("up", "down"),
            ("good", "bad"),
            ("yes", "no"),
            ("love", "hate"),
            ("day", "night"),
            ("black", "white"),
            ("rich", "poor"),
            ("fast", "slow")
        };

        var pairs = new[] { (a, b), (b, a) };

        return nonSynonyms.Any(p =>
            p.Item1 == a && p.Item2 == b ||
            p.Item1 == b && p.Item2 == a);
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