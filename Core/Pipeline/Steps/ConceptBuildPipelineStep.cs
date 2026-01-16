namespace DictionaryImporter.Core.Pipeline.Steps;

public sealed class ConceptBuildPipelineStep(DictionaryConceptBuilder conceptBuilder) : IImportPipelineStep
{
    public string Name => PipelineStepNames.ConceptBuild;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await conceptBuilder.BuildAsync(context.SourceCode, context.CancellationToken);
    }
}