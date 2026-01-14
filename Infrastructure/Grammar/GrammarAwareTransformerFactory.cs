namespace DictionaryImporter.Infrastructure.Grammar;

public sealed class GrammarAwareTransformerFactory(
    IGrammarCorrector grammarCorrector,
    GrammarCorrectionSettings settings,
    ILoggerFactory loggerFactory)
{
    public IDataTransformer<T> CreateGrammarAwareTransformer<T>(IDataTransformer<T> innerTransformer)
    {
        return new GrammarAwareTransformerWrapper<T>(
            innerTransformer,
            grammarCorrector,
            settings,
            loggerFactory.CreateLogger<GrammarAwareTransformerWrapper<T>>());
    }
}