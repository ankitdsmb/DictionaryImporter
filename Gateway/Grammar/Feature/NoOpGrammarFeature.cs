namespace DictionaryImporter.Gateway.Grammar.Feature;

public sealed class NoOpGrammarFeature : IGrammarFeature
{
    public Task CorrectSourceAsync(string sourceCode, CancellationToken ct)
        => Task.CompletedTask;

    public Task<string> CleanAsync(
        string input,
        string? languageCode,
        bool applySpellCheck,
        CancellationToken ct)
        => Task.FromResult(input);
}