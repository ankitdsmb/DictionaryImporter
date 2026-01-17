using DictionaryImporter.AITextKit.Grammar.Core.Models;
using DictionaryImporter.AITextKit.Grammar.Core.Results;

namespace DictionaryImporter.AITextKit.Grammar.Core;

public interface IGrammarCorrector
{
    Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default);
}