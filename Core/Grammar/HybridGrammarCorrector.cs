using DictionaryImporter.Infrastructure.Grammar.Helper;

namespace DictionaryImporter.Core.Grammar;

public sealed class HybridGrammarCorrector(
    ILanguageDetector languageDetector,
    IGrammarCorrector languageToolCorrector,
    ILogger<HybridGrammarCorrector> logger) : IGrammarCorrector
{
    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);

        languageCode ??= languageDetector.Detect(text);

        if (text.Length < 10)
        {
            var spellChecker = new NHunspellSpellChecker(languageCode);
            if (spellChecker.IsSupported)
            {
                return await SpellCheckTextAsync(text, spellChecker, ct);
            }
        }

        return await languageToolCorrector.CheckAsync(text, languageCode, ct);
    }

    private async Task<GrammarCheckResult> SpellCheckTextAsync(string text, ISpellChecker spellChecker, CancellationToken ct)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var issues = new List<GrammarIssue>();

        int currentPosition = 0;

        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();

            var wordPosition = text.IndexOf(word, currentPosition, StringComparison.OrdinalIgnoreCase);
            if (wordPosition == -1)
            {
                wordPosition = currentPosition;
                currentPosition += word.Length + 1;
            }
            else
            {
                currentPosition = wordPosition + word.Length;
            }

            var result = spellChecker.Check(word);
            if (!result.IsCorrect)
            {
                var issue = GrammarIssueHelper.CreateSpellingIssue(wordPosition, wordPosition + word.Length, word, result.Suggestions, GetWordContext(text, wordPosition));
                issues.Add(issue);
            }
        }

        return new GrammarCheckResult(
            issues.Count > 0,
            issues.Count,
            issues,
            TimeSpan.Zero
        );
    }

    private string GetWordContext(string text, int position, int contextLength = 50)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var start = Math.Max(0, position - contextLength);
        var end = Math.Min(text.Length, position + contextLength);
        return text.Substring(start, end - start);
    }

    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = null, CancellationToken ct = default)
    {
        languageCode ??= languageDetector.Detect(text);
        return languageToolCorrector.AutoCorrectAsync(text, languageCode, ct);
    }

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = null, CancellationToken ct = default)
    {
        languageCode ??= languageDetector.Detect(text);
        return languageToolCorrector.SuggestImprovementsAsync(text, languageCode, ct);
    }
}