namespace DictionaryImporter.Core.Pipeline.Steps
{
    public sealed class GraphBuildPipelineStep(
        DictionaryGraphNodeBuilder nodeBuilder,
        DictionaryGraphBuilder graphBuilder) : IImportPipelineStep
    {
        public string Name => PipelineStepNames.GraphBuild;

        public async Task ExecuteAsync(ImportPipelineContext context)
        {
            await nodeBuilder.BuildAsync(context.SourceCode, context.CancellationToken);
            await graphBuilder.BuildAsync(context.SourceCode, context.CancellationToken);
        }
    }
}