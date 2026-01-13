// File: DictionaryImporter.Core/Grammar/Enhanced/IGrammarEngine.cs
namespace DictionaryImporter.Core.Grammar.Enhanced;

public interface IGrammarEngine
{
    string Name { get; }
    double ConfidenceWeight { get; }

    bool IsSupported(string languageCode);

    Task InitializeAsync();

    Task<GrammarCheckResult> CheckAsync(string text, string languageCode, CancellationToken ct);

    Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode, CancellationToken ct);
}

public interface ITrainableGrammarEngine : IGrammarEngine
{
    Task<bool> TrainAsync(GrammarFeedback feedback, CancellationToken ct);

    bool CanTrain { get; }
}