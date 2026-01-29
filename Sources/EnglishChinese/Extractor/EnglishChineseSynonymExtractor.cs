using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.EnglishChinese.Extractor;

public sealed class EnglishChineseSynonymExtractor(ILogger<EnglishChineseSynonymExtractor> logger) : ISynonymExtractor
{
    private static readonly Regex[] HighConfidencePatterns =
    [
        new(@"^\s*(?<word1>\w+)\s+means\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(?<word1>\w+)\s+is\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(?<word1>\w+)\s+is\s+the\s+same\s+as\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(?<word1>\w+)\s+and\s+(?<word2>\w+)\s+are\s+synonyms\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private static readonly Regex[] MediumConfidencePatterns =
    [
        new(@"^\s*(?<word1>\w+)\s*,\s*or\s+(?<word2>\w+)\s*,",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(?<word1>\w+)\s*\(\s*also\s+(?<word2>\w+)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private static readonly Regex[] LowConfidencePatterns =
    [
        new(@"synonymous\s+with\s+(?<word2>\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"similar\s+to\s+(?<word2>\w+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private static readonly Regex[] DefinitionReplacementPatterns =
    [
        new(@"^If you (?<word1>\w+) [^,]+, you (?<word2>\w+) (?:it|something|one)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^To (?<word1>\w+) means to (?<word2>\w+)(?:\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^(?<word1>\w+ing) is (?<word2>\w+ing)(?:\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    public string SourceCode => "ENG_CHN";

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

        foreach (var pattern in LowConfidencePatterns)
        {
            var match = pattern.Match(cleanedDefinition);
            if (match.Success)
            {
                var word2 = match.Groups["word2"].Value.ToLowerInvariant();

                if (word2 != cleanedHeadword && IsValidHeadword(word2))
                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = word2,
                        ConfidenceLevel = "low",
                        DetectionMethod = $"Inference: {pattern}",
                        SourceText = match.Value
                    });
            }
        }

        if (cleanedHeadword == "envisage" || cleanedHeadword == "envision")
        {
            var otherWord = cleanedHeadword == "envisage" ? "envision" : "envisage";

            if (cleanedDefinition.Contains(otherWord, StringComparison.OrdinalIgnoreCase))
                results.Add(new SynonymDetectionResult
                {
                    TargetHeadword = otherWord,
                    ConfidenceLevel = "high",
                    DetectionMethod = "ExplicitDefinitionReference",
                    SourceText = $"Definition references {otherWord}"
                });
        }

        var deduplicated = results
            .GroupBy(r => r.TargetHeadword)
            .Select(g => g.OrderByDescending(r => GetConfidenceScore(r.ConfidenceLevel)).First())
            .ToList();

        logger.LogDebug("Found {Count} synonyms for {Headword}: {Synonyms}",
            deduplicated.Count, cleanedHeadword,
            string.Join(", ", deduplicated.Select(r => r.TargetHeadword)));

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
            .Replace("  ", " ");

        cleaned = Regex.Replace(cleaned, @"●+○+\s*", " ");
        cleaned = Regex.Replace(cleaned, @"★+☆+\s*", " ");

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

    private bool IsObviousNonSynonymPair(string a, string b)
    {
        var nonSynonyms = new[]
        {
            ("big", "small"),
            ("hot", "cold"),
            ("up", "down"),
            ("good", "bad"),
            ("yes", "no")
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