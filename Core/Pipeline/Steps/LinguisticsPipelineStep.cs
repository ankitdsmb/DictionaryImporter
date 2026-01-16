namespace DictionaryImporter.Core.Pipeline.Steps;

public sealed class LinguisticsPipelineStep(DictionaryEntryLinguisticEnricher enricher) : IImportPipelineStep
{
    public string Name => PipelineStepNames.Linguistics;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await enricher.ExecuteAsync(context.SourceCode, context.CancellationToken);
    }
}