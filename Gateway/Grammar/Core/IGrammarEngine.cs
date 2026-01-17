using DictionaryImporter.Gateway.Grammar.Core.Results;

namespace DictionaryImporter.Gateway.Grammar.Core
{
    public interface IGrammarEngine
    {
        string Name { get; }
        double ConfidenceWeight { get; }

        Task InitializeAsync();

        Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

        bool IsSupported(string languageCode);
    }
}