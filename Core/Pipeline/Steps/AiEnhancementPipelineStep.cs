namespace DictionaryImporter.Core.Pipeline.Steps;

public sealed class AiEnhancementPipelineStep(AiEnhancementStep step) : IImportPipelineStep
{
    public string Name => PipelineStepNames.AiEnhancement;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await step.ExecuteAsync(context.SourceCode, context.CancellationToken);
    }
}