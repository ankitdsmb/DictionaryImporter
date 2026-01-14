namespace DictionaryImporter.Core.Grammar.Enhanced
{
    public interface ITrainableGrammarEngine : IGrammarEngine
    {
        Task<bool> TrainAsync(GrammarFeedback feedback, CancellationToken ct);

        bool CanTrain { get; }
    }
}