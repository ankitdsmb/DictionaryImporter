// File: DictionaryImporter.Infrastructure/Grammar/Enhanced/GrammarBlender.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using System.Collections.Concurrent;

namespace DictionaryImporter.Infrastructure.Grammar.Enhanced;

public sealed class GrammarBlender
{
    private readonly BlendingStrategy _strategy;
    private readonly Dictionary<string, double> _engineWeights;

    public GrammarBlender(BlendingStrategy strategy)
    {
        _strategy = strategy;
        _engineWeights = new Dictionary<string, double>
        {
            ["LanguageTool"] = 0.85,
            ["NHunspell"] = 0.95,
            ["PatternRules"] = 0.90,
            ["NTextCat"] = 0.80
        };
    }

    public IReadOnlyList<GrammarIssue> BlendIssues(IReadOnlyList<GrammarCheckResult> engineResults)
    {
        return _strategy switch
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

        // Group issues by position
        var issueGroups = new Dictionary<string, List<GrammarIssue>>();

        foreach (var result in engineResults)
        {
            foreach (var issue in result.Issues)
            {
                var key = $"{issue.StartOffset}-{issue.EndOffset}";

                if (!issueGroups.ContainsKey(key))
                    issueGroups[key] = new List<GrammarIssue>();

                issueGroups[key].Add(issue);
            }
        }

        // Apply corrections from end to start
        foreach (var group in issueGroups.Values.OrderByDescending(g => g.First().StartOffset))
        {
            if (group.Count >= 2) // At least 2 engines agree
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
            _strategy
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

        // Group similar issues
        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                var key = $"{issue.StartOffset}-{issue.EndOffset}-{issue.RuleId}";

                if (!issueGroups.ContainsKey(key))
                    issueGroups[key] = new List<GrammarIssue>();

                issueGroups[key].Add(issue);
            }
        }

        // Weighted consensus
        foreach (var group in issueGroups.Values)
        {
            if (group.Count >= 2) // At least 2 engines agree
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

        // Collect all issues by position
        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                var positionKey = $"{issue.StartOffset}-{issue.EndOffset}";
                issuesByPosition.AddOrUpdate(
                    positionKey,
                    _ => new List<GrammarIssue> { issue },
                    (_, list) => { list.Add(issue); return list; }
                );
            }
        }

        // Keep only issues where majority of engines agree
        var blended = new List<GrammarIssue>();
        foreach (var kvp in issuesByPosition)
        {
            if (kvp.Value.Count >= (results.Count / 2) + 1) // Majority
            {
                // Take the issue with highest confidence
                var bestIssue = kvp.Value.OrderByDescending(i => i.ConfidenceLevel).First();
                blended.Add(bestIssue);
            }
        }

        return blended;
    }

    private IReadOnlyList<GrammarIssue> PriorityBlend(IReadOnlyList<GrammarCheckResult> results)
    {
        // Simple priority: use the primary engine's results
        var primaryEngine = "LanguageTool"; // Could be configurable

        // Find the primary engine's results by checking each engine
        foreach (var result in results)
        {
            if (result.Issues.Any())
            {
                // In a real implementation, you would track which engine produced which result
                // For now, we'll use the first result with issues
                return result.Issues;
            }
        }

        return new List<GrammarIssue>();
    }

    private IReadOnlyList<GrammarIssue> DefaultBlend(IReadOnlyList<GrammarCheckResult> results)
    {
        // Union of all issues, removing duplicates
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