using DictionaryImporter.Gateway.Grammar.Feature;

namespace DictionaryImporter.Core.Pipeline.Steps
{
    public sealed class GrammarCorrectionPipelineStep(IGrammarFeature grammar) : IImportPipelineStep
    {
        public string Name => PipelineStepNames.GrammarCorrection;

        public async Task ExecuteAsync(ImportPipelineContext context)
        {
            await grammar.CorrectSourceAsync(context.SourceCode, context.CancellationToken);
        }
    }
}