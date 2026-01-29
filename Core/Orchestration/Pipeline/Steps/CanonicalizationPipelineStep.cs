using DictionaryImporter.Core.Orchestration.Models;

namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class CanonicalizationPipelineStep(ICanonicalWordResolver canonicalResolver) : IImportPipelineStep
{
    public string Name => PipelineStepNames.Canonicalization;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await canonicalResolver.ResolveAsync(context.SourceCode, context.CancellationToken);
    }
}