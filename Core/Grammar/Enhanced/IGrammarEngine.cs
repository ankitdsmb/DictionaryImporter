// File: DictionaryImporter.Core/Grammar/Enhanced/IGrammarEngine.cs
namespace DictionaryImporter.Core.Grammar.Enhanced;

public interface IGrammarEngine
{
    string Name { get; }
    double ConfidenceWeight { get; }

    Task InitializeAsync();

    Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    bool IsSupported(string languageCode);
}

public interface ITrainableGrammarEngine : IGrammarEngine
{
    Task<bool> TrainAsync(GrammarFeedback feedback, CancellationToken ct);

    bool CanTrain { get; }
}