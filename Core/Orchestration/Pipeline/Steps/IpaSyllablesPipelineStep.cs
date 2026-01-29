using DictionaryImporter.Core.Orchestration.Models;

namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class IpaSyllablesPipelineStep(CanonicalWordSyllableEnricher syllableEnricher) : IImportPipelineStep
{
    public string Name => PipelineStepNames.IpaSyllables;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await syllableEnricher.ExecuteAsync(context.CancellationToken);
    }
}