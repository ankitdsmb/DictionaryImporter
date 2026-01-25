namespace DictionaryImporter.Gateway.Grammar.Feature;

public interface IGrammarFeature
{
    Task CorrectSourceAsync(string sourceCode, CancellationToken ct);

    Task<string> CleanAsync(
        string text,
        string? languageCode = null,
        bool applyAutoCorrection = true,
        CancellationToken ct = default);
}