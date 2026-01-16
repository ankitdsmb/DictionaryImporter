namespace DictionaryImporter.AITextKit.Grammar.Infrastructure.Enhanced;

public sealed class GrammarBlender(BlendingStrategy strategy)
{
    private readonly Dictionary<string, double> _engineWeights = new()
    {
        ["LanguageTool"] = 0.85,
        ["NHunspell"] = 0.95,
        ["PatternRules"] = 0.90,
        ["NTextCat"] = 0.80
    };

    public IReadOnlyList<GrammarIssue> BlendIssues(IReadOnlyList<GrammarCheckResult> engineResults)
    {
        return strategy switch
        {
            BlendingStrategy.ConfidenceWeighted => WeightedBlend(engineResults),
            BlendingStrategy.MajorityVote => MajorityVoteBlend(engineResults),
            BlendingStrategy.EnginePriority => PriorityBlend(engineResults),
            _ => DefaultBlend(engineResults)
        };
    }

    public GrammarBlendedResult BlendCorrections(IReadOnlyList<GrammarCheckResult> engineResults, string originalText)
    {
        var corrections = new List<BlendedCorrection>();
        var correctedText = originalText;

        var issueGroups = new Dictionary<string, List<GrammarIssue>>();

        foreach (var result in engineResults)
        {
            foreach (var issue in result.Issues)
            {
                var key = $"{issue.StartOffset}-{issue.EndOffset}";

                if (!issueGroups.ContainsKey(key))
                    issueGroups[key] = [];

                issueGroups[key].Add(issue);
            }
        }

        foreach (var group in issueGroups.Values.OrderByDescending(g => g.First().StartOffset))
        {
            if (group.Count >= 2)
            {
                var bestIssue = group
                    .OrderByDescending(i => i.ConfidenceLevel)
                    .First();

                if (bestIssue.Replacements.Count > 0)
                {
                    var replacement = bestIssue.Replacements[0];
                    var originalSegment = correctedText.Substring(
                        bestIssue.StartOffset,
                        bestIssue.EndOffset - bestIssue.StartOffset);

                    correctedText = correctedText.Remove(
                        bestIssue.StartOffset,
                        bestIssue.EndOffset - bestIssue.StartOffset)
                        .Insert(bestIssue.StartOffset, replacement);

                    corrections.Add(new BlendedCorrection(
                        group.Select(i => new EngineSuggestion(
                            GetEngineName(i.RuleId),
                            i.Replacements.FirstOrDefault() ?? "",
                            i.ConfidenceLevel,
                            i.RuleId,
                            i.Message
                        )).ToList(),
                        replacement,
                        group.Average(i => i.ConfidenceLevel),
                        $"Consensus from {group.Count} engines"
                    ));
                }
            }
        }

        return new GrammarBlendedResult(
            originalText,
            correctedText,
            corrections,
            strategy
        );
    }

    private string GetEngineName(string ruleId)
    {
        return ruleId switch
        {
            string s when s.StartsWith("PATTERN_") => "PatternRules",
            string s when s.StartsWith("SPELLING_") => "NHunspell",
            string s when s.StartsWith("LANGUAGE_") => "NTextCat",
            _ => "LanguageTool"
        };
    }

    private IReadOnlyList<GrammarIssue> WeightedBlend(IReadOnlyList<GrammarCheckResult> results)
    {
        var blended = new List<GrammarIssue>();
        var issueGroups = new Dictionary<string, List<GrammarIssue>>();

        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                var key = $"{issue.StartOffset}-{issue.EndOffset}-{issue.RuleId}";

                if (!issueGroups.ContainsKey(key))
                    issueGroups[key] = [];

                issueGroups[key].Add(issue);
            }
        }

        foreach (var group in issueGroups.Values)
        {
            if (group.Count >= 2)
            {
                var weightedConfidence = group.Average(i => i.ConfidenceLevel);
                var consensusIssue = group
                    .OrderByDescending(i => i.ConfidenceLevel)
                    .First();

                blended.Add(consensusIssue with
                {
                    ConfidenceLevel = (int)weightedConfidence
                });
            }
        }

        return blended;
    }

    private IReadOnlyList<GrammarIssue> MajorityVoteBlend(IReadOnlyList<GrammarCheckResult> results)
    {
        var issuesByPosition = new ConcurrentDictionary<string, List<GrammarIssue>>();

        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                var positionKey = $"{issue.StartOffset}-{issue.EndOffset}";
                issuesByPosition.AddOrUpdate(
                    positionKey,
                    _ => [issue],
                    (_, list) => { list.Add(issue); return list; }
                );
            }
        }

        var blended = new List<GrammarIssue>();
        foreach (var kvp in issuesByPosition)
        {
            if (kvp.Value.Count >= results.Count / 2 + 1)
            {
                var bestIssue = kvp.Value.OrderByDescending(i => i.ConfidenceLevel).First();
                blended.Add(bestIssue);
            }
        }

        return blended;
    }

    private IReadOnlyList<GrammarIssue> PriorityBlend(IReadOnlyList<GrammarCheckResult> results)
    {
        var primaryEngine = "LanguageTool";

        foreach (var result in results)
        {
            if (result.Issues.Any())
            {
                return result.Issues;
            }
        }

        return new List<GrammarIssue>();
    }

    private IReadOnlyList<GrammarIssue> DefaultBlend(IReadOnlyList<GrammarCheckResult> results)
    {
        var allIssues = new Dictionary<string, GrammarIssue>();

        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                var key = $"{issue.StartOffset}-{issue.EndOffset}-{issue.RuleId}";
                if (!allIssues.ContainsKey(key) ||
                    issue.ConfidenceLevel > allIssues[key].ConfidenceLevel)
                {
                    allIssues[key] = issue;
                }
            }
        }

        return allIssues.Values.ToList();
    }
}