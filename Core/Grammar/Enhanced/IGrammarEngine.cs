namespace DictionaryImporter.Core.Grammar.Enhanced;

public interface IGrammarEngine
{
    string Name { get; }
    double ConfidenceWeight { get; }

    Task InitializeAsync();

    Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    bool IsSupported(string languageCode);
}