namespace DictionaryImporter.Core.Grammar.Simple;

public sealed class NoOpGrammarCorrector : IGrammarCorrector
{
    public Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        return Task.FromResult(new GrammarCheckResult(
            false,
            0,
            [],
            TimeSpan.Zero));
    }

    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        return Task.FromResult(new GrammarCorrectionResult(
            text,
            text,
            [],
            []));
    }

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<GrammarSuggestion>>([]);
    }
}