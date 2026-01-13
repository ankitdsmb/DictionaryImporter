namespace DictionaryImporter.Core.Grammar;

public sealed class HybridGrammarCorrector(
    ILanguageDetector languageDetector,
    IGrammarCorrector languageToolCorrector,
    ILogger<HybridGrammarCorrector> logger) : IGrammarCorrector
{
    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        // If languageCode is not provided, detect it
        languageCode ??= languageDetector.Detect(text);

        // For short texts, we might rely on spell checking only
        if (text.Length < 10)
        {
            // Use NHunspell if available for the language
            var spellChecker = new NHunspellSpellChecker(languageCode);
            if (spellChecker.IsSupported)
            {
                return await SpellCheckTextAsync(text, spellChecker, ct);
            }
        }

        // For longer texts, use LanguageTool
        return await languageToolCorrector.CheckAsync(text, languageCode, ct);
    }

    private async Task<GrammarCheckResult> SpellCheckTextAsync(string text, ISpellChecker spellChecker, CancellationToken ct)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var issues = new List<GrammarIssue>();

        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();
            var result = spellChecker.Check(word);
            if (!result.IsCorrect)
            {
                issues.Add(new GrammarIssue(
                    "SPELLING",
                    $"Spelling error: {word}",
                    "SPELLING",
                    0, // We don't have offset in this simple check
                    word.Length,
                    result.Suggestions.ToList(),
                    90
                ));
            }
        }

        return new GrammarCheckResult(
            issues.Count > 0,
            issues.Count,
            issues,
            TimeSpan.Zero
        );
    }

    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = null, CancellationToken ct = default)
    {
        // For now, delegate to LanguageTool. We can enhance with NHunspell for spelling.
        languageCode ??= languageDetector.Detect(text);
        return languageToolCorrector.AutoCorrectAsync(text, languageCode, ct);
    }

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = null, CancellationToken ct = default)
    {
        languageCode ??= languageDetector.Detect(text);
        return languageToolCorrector.SuggestImprovementsAsync(text, languageCode, ct);
    }
}