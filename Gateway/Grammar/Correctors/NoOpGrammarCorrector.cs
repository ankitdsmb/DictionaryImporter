using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;

namespace DictionaryImporter.Gateway.Grammar.Correctors;

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