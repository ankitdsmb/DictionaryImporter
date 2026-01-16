using DictionaryImporter.AITextKit.Grammar.Infrastructure.Helper;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.AITextKit.Grammar.Enhanced;

public sealed class HunspellCorrectorAdapter(
    ILanguageDetector languageDetector,
    EnhancedGrammarConfiguration settings,
    ILogger<HunspellCorrectorAdapter> logger)
    : IGrammarCorrector
{
    public Task<GrammarCheckResult> CheckAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(new GrammarCheckResult(false, 0, [], TimeSpan.Zero));
    }

    public Task<GrammarCorrectionResult> AutoCorrectAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: text,
                AppliedCorrections: [],
                RemainingIssues: []));
        }

        try
        {
            languageCode ??= languageDetector.Detect(text) ?? settings.DefaultLanguage;

            var spellChecker = new NHunspellSpellChecker(languageCode);

            if (!spellChecker.IsSupported)
            {
                return Task.FromResult(new GrammarCorrectionResult(
                    OriginalText: text,
                    CorrectedText: text,
                    AppliedCorrections: [],
                    RemainingIssues: []));
            }

            var corrected = SplitJoinedWords(text, spellChecker, minTokenLength: 6);

            if (string.Equals(corrected, text, StringComparison.Ordinal))
            {
                return Task.FromResult(new GrammarCorrectionResult(
                    OriginalText: text,
                    CorrectedText: text,
                    AppliedCorrections: [],
                    RemainingIssues: []));
            }

            return Task.FromResult(new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: corrected,
                AppliedCorrections:
                [
                    new AppliedCorrection(
                        "HUNSPELL_SPLIT",
                        "Hunspell Split",
                        "Split joined OCR words using NHunspell dictionary",
                        corrected,
                        85)
                ],
                RemainingIssues: []));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Hunspell correction failed");
            return Task.FromResult(new GrammarCorrectionResult(
                OriginalText: text,
                CorrectedText: text,
                AppliedCorrections: [],
                RemainingIssues: []));
        }
    }

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
        string text,
        string? languageCode = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GrammarSuggestion>>([]);

    private static string SplitJoinedWords(
        string input,
        ISpellChecker spellChecker,
        int minTokenLength)
    {
        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var changed = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Length < minTokenLength)
                continue;

            // If valid already, skip
            if (spellChecker.Check(token).IsCorrect)
                continue;

            // Try split token into 2 valid words
            for (var split = 2; split <= token.Length - 2; split++)
            {
                var left = token[..split];
                var right = token[split..];

                if (spellChecker.Check(left).IsCorrect && spellChecker.Check(right).IsCorrect)
                {
                    tokens[i] = left;
                    tokens.Insert(i + 1, right);
                    changed = true;
                    break;
                }
            }
        }

        return changed ? string.Join(" ", tokens) : input;
    }
}