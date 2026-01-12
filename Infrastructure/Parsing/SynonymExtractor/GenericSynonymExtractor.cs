using DictionaryImporter.Core.Parsing;

namespace DictionaryImporter.Infrastructure.Parsing.SynonymExtractor;

public sealed class GenericSynonymExtractor : ISynonymExtractor
{
    private static readonly Regex[] SafePatterns =
    {
        new(@"^\s*(?<word1>\w+)\s+means\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(?<word1>\w+)\s+is\s+(?<word2>\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    public string SourceCode => "*";

    public IReadOnlyList<SynonymDetectionResult> Extract(
        string headword,
        string definition,
        string? rawDefinition = null)
    {
        // Generic extractor is conservative - only catches obvious cases
        var results = new List<SynonymDetectionResult>();

        if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(headword))
            return results;

        var cleanedHeadword = headword.ToLowerInvariant().Trim();
        var cleanedDefinition = definition.Trim();

        foreach (var pattern in SafePatterns)
        {
            var match = pattern.Match(cleanedDefinition);
            if (match.Success)
            {
                var word1 = match.Groups["word1"].Value.ToLowerInvariant();
                var word2 = match.Groups["word2"].Value.ToLowerInvariant();

                if (word1 == cleanedHeadword && IsValidHeadword(word2) && word2 != word1)
                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = word2,
                        ConfidenceLevel = "medium",
                        DetectionMethod = $"GenericPattern: {pattern}",
                        SourceText = match.Value
                    });
                else if (word2 == cleanedHeadword && IsValidHeadword(word1) && word1 != word2)
                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = word1,
                        ConfidenceLevel = "medium",
                        DetectionMethod = $"GenericPattern: {pattern}",
                        SourceText = match.Value
                    });
            }
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

        return true;
    }

    private static bool IsValidHeadword(string word)
    {
        return !string.IsNullOrWhiteSpace(word) &&
               word.Length >= 2 &&
               word.All(c => char.IsLetter(c) || c == '-');
    }
}