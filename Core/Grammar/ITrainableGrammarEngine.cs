namespace DictionaryImporter.Core.Grammar;

public interface ITrainableGrammarEngine
{
    Task TrainAsync(GrammarFeedback feedback, CancellationToken ct = default);
}