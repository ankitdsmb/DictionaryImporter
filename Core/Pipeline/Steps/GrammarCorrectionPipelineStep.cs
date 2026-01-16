namespace DictionaryImporter.Core.Pipeline.Steps;

public sealed class GrammarCorrectionPipelineStep(GrammarCorrectionStep step) : IImportPipelineStep
{
    public string Name => PipelineStepNames.GrammarCorrection;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await step.ExecuteAsync(context.SourceCode, context.CancellationToken);
    }
}