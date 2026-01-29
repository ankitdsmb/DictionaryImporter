using DictionaryImporter.Core.Orchestration.Models;

namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class GraphValidationPipelineStep(DictionaryGraphValidator graphValidator) : IImportPipelineStep
{
    public string Name => PipelineStepNames.GraphValidation;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await graphValidator.ValidateAsync(context.SourceCode, context.CancellationToken);
    }
}