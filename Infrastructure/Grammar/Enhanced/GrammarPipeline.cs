using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Configuration;
using DictionaryImporter.Core.Grammar.Enhanced;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DictionaryImporter.Infrastructure.Grammar.Enhanced;

public sealed class GrammarPipeline : IGrammarPipeline
{
    private readonly ConcurrentDictionary<string, IGrammarEngine> _engines = new();
    private readonly ILogger<GrammarPipeline> _logger;
    private readonly GrammarPipelineConfiguration _config;
    private readonly CustomRuleEngine _customRules;
    private readonly GrammarBlender _blender;

    public GrammarPipeline(
        IEnumerable<IGrammarEngine> engines,
        GrammarPipelineConfiguration config,
        ILogger<GrammarPipeline> logger)
    {
        _config = config;
        _logger = logger;
        _customRules = new CustomRuleEngine(config.CustomRulesPath);
        _blender = new GrammarBlender(config.BlendingStrategy);

        foreach (var engine in engines)
        {
            _engines[engine.Name] = engine;
        }

        InitializeEngines();
    }

    private void InitializeEngines()
    {
        // Initialize all engines in parallel
        var initTasks = _engines.Values.Select(e => e.InitializeAsync());
        Task.WhenAll(initTasks).Wait();
    }

    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        var sw = Stopwatch.StartNew();

        // Run all engines in parallel
        var engineTasks = _engines.Values
            .Where(e => e.IsSupported(languageCode))
            .Select(engine => engine.CheckAsync(text, languageCode, ct))
            .ToList();

        await Task.WhenAll(engineTasks);

        // Merge results using blending strategy
        var engineResults = engineTasks
            .Where(t => t.IsCompletedSuccessfully)
            .Select(t => t.Result)
            .ToList();

        var blendedIssues = _blender.BlendIssues(engineResults);

        sw.Stop();

        return new GrammarCheckResult(
            blendedIssues.Any(),
            blendedIssues.Count,
            blendedIssues,
            sw.Elapsed
        );
    }

    public async Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCorrectionResult(text, text, Array.Empty<AppliedCorrection>(), Array.Empty<GrammarIssue>());

        var checkResult = await CheckAsync(text, languageCode, ct);
        if (!checkResult.HasIssues)
            return new GrammarCorrectionResult(text, text, Array.Empty<AppliedCorrection>(), Array.Empty<GrammarIssue>());

        // Apply only safe, high-confidence corrections
        var safeIssues = checkResult.Issues
            .Where(issue => issue.ConfidenceLevel >= _config.MinimumConfidence)
            .OrderByDescending(i => i.StartOffset) // Apply from end to start
            .ToList();

        var correctedText = text;
        var appliedCorrections = new List<AppliedCorrection>();

        foreach (var issue in safeIssues)
        {
            if (issue.Replacements.Count == 0)
                continue;

            var replacement = issue.Replacements[0];
            var originalSegment = correctedText.Substring(issue.StartOffset,
                issue.EndOffset - issue.StartOffset);

            correctedText = correctedText.Remove(issue.StartOffset,
                issue.EndOffset - issue.StartOffset)
                .Insert(issue.StartOffset, replacement);

            appliedCorrections.Add(new AppliedCorrection(
                originalSegment,
                replacement,
                issue.RuleId,
                issue.Message,
                issue.ConfidenceLevel
            ));
        }

        var remainingIssues = checkResult.Issues
            .Where(issue => !safeIssues.Contains(issue))
            .ToList();

        return new GrammarCorrectionResult(
            text,
            correctedText,
            appliedCorrections,
            remainingIssues
        );
    }

    public async Task<GrammarPipelineDiagnostics> AnalyzeAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var contributions = new Dictionary<string, EngineContribution>();

        foreach (var (name, engine) in _engines)
        {
            if (!engine.IsSupported(languageCode))
                continue;

            var engineSw = Stopwatch.StartNew();
            var result = await engine.CheckAsync(text, languageCode, ct);
            engineSw.Stop();

            contributions[name] = new EngineContribution(
                name,
                result.Issues.Count,
                engineSw.Elapsed,
                name == _config.PrimaryEngine,
                engine.ConfidenceWeight
            );
        }

        var allIssues = contributions.Values.Sum(c => c.IssuesFound);
        var categoryBreakdown = AnalyzeIssueCategories(contributions);

        sw.Stop();

        return new GrammarPipelineDiagnostics(
            contributions,
            sw.Elapsed,
            allIssues,
            categoryBreakdown
        );
    }

    public async Task<GrammarBlendedResult> GetBlendedCorrectionsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        var engineTasks = _engines.Values
            .Where(e => e.IsSupported(languageCode))
            .Select(engine => engine.CheckAsync(text, languageCode, ct))
            .ToList();

        await Task.WhenAll(engineTasks);

        var engineResults = engineTasks
            .Where(t => t.IsCompletedSuccessfully)
            .Select(t => t.Result)
            .ToList();

        var blendedCorrections = _blender.BlendCorrections(engineResults, text);

        return blendedCorrections;
    }

    public async Task<bool> TrainFromFeedbackAsync(GrammarFeedback feedback, CancellationToken ct = default)
    {
        await _customRules.TrainAsync(feedback);

        foreach (var engine in _engines.Values.OfType<ITrainableGrammarEngine>())
        {
            await engine.TrainAsync(feedback, ct);
        }

        return true;
    }

    public async Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        // Use custom rules and pattern matching for suggestions
        var suggestions = new List<GrammarSuggestion>();

        // Check for long sentences
        if (text.Length > 100 && text.Count(c => c == '.') < 2)
        {
            suggestions.Add(new GrammarSuggestion(
                text,
                "Consider breaking this into shorter sentences for better readability.",
                "Long sentences can be difficult to parse.",
                "clarity"
            ));
        }

        // Check for passive voice (simple detection)
        if (text.Contains(" is ") || text.Contains(" was ") || text.Contains(" were "))
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.EndsWith("ed") && w.Length > 3))
            {
                suggestions.Add(new GrammarSuggestion(
                    text,
                    "Consider using active voice for more direct statements.",
                    "Passive voice can reduce clarity and impact.",
                    "clarity"
                ));
            }
        }

        return suggestions;
    }

    private Dictionary<string, int> AnalyzeIssueCategories(IReadOnlyDictionary<string, EngineContribution> contributions)
    {
        // Simplified implementation - you can enhance this
        var categories = new Dictionary<string, int>
        {
            ["SPELLING"] = 0,
            ["GRAMMAR"] = 0,
            ["PUNCTUATION"] = 0,
            ["STYLE"] = 0
        };

        // This would need to analyze actual issues from each engine
        return categories;
    }
}