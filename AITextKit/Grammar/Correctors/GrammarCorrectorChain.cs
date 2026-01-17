using DictionaryImporter.AITextKit.Grammar.Core;
using DictionaryImporter.AITextKit.Grammar.Core.Models;
using DictionaryImporter.AITextKit.Grammar.Core.Results;
using DictionaryImporter.AITextKit.Grammar.Feature;

namespace DictionaryImporter.AITextKit.Grammar.Correctors;

public sealed class GrammarCorrectorChain(
    IEnumerable<IGrammarCorrector> correctors,
    ILogger<GrammarCorrectorChain> logger)
    : IGrammarCorrector
{
    private readonly List<IGrammarCorrector> _correctors =
        correctors
            .Where(x => x is not GrammarFeature) // prevent recursion
            .ToList();

    public Task<GrammarCheckResult> CheckAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        // GrammarFeature handles check via IGrammarEngine(s).
        return Task.FromResult(new GrammarCheckResult(false, 0, [], TimeSpan.Zero));
    }

    public async Task<GrammarCorrectionResult> AutoCorrectAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: text,
                AppliedCorrections: [],
                RemainingIssues: []);
        }

        var current = text;

        var applied = new List<AppliedCorrection>();
        var remaining = new List<GrammarIssue>();

        foreach (var corrector in _correctors)
        {
            try
            {
                var result = await corrector.AutoCorrectAsync(current, languageCode, ct);

                // ✅ update current
                if (!string.IsNullOrWhiteSpace(result.CorrectedText))
                    current = result.CorrectedText;

                // ✅ merge applied corrections
                if (result.AppliedCorrections is { Count: > 0 })
                    applied.AddRange(result.AppliedCorrections);

                // ✅ merge remaining issues
                if (result.RemainingIssues is { Count: > 0 })
                    remaining.AddRange(result.RemainingIssues);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Corrector failed | Corrector={Corrector}", corrector.GetType().Name);
            }
        }

        return new GrammarCorrectionResult(
            OriginalText: text,
            CorrectedText: current,
            AppliedCorrections: applied,
            RemainingIssues: remaining);
    }

    public async Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var all = new List<GrammarSuggestion>();

        foreach (var corrector in _correctors)
        {
            try
            {
                var suggestions = await corrector.SuggestImprovementsAsync(text, languageCode, ct);
                if (suggestions is { Count: > 0 })
                    all.AddRange(suggestions);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Suggest failed | Corrector={Corrector}", corrector.GetType().Name);
            }
        }

        return all;
    }
}