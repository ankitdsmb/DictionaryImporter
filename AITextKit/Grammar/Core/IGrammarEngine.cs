using DictionaryImporter.AITextKit.Grammar.Core.Results;

namespace DictionaryImporter.AITextKit.Grammar.Core;

public interface IGrammarEngine
{
    string Name { get; }
    double ConfidenceWeight { get; }

    Task InitializeAsync();

    Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    bool IsSupported(string languageCode);
}