namespace DictionaryImporter.Core.Pipeline.Steps
{
    public sealed class ConceptMergePipelineStep(
        DictionaryConceptMerger conceptMerger,
        DictionaryConceptConfidenceCalculator confidenceCalculator,
        DictionaryGraphRankCalculator graphRankCalculator) : IImportPipelineStep
    {
        public string Name => PipelineStepNames.ConceptMerge;

        public async Task ExecuteAsync(ImportPipelineContext context)
        {
            await conceptMerger.MergeAsync(context.CancellationToken);
            await confidenceCalculator.CalculateAsync(context.CancellationToken);
            await graphRankCalculator.CalculateAsync(context.CancellationToken);
        }
    }
}