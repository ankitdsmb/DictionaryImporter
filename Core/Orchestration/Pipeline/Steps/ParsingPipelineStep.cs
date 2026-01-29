using DictionaryImporter.Core.Orchestration.Models;

namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class ParsingPipelineStep(DictionaryParsedDefinitionProcessor processor) : IImportPipelineStep
{
    public string Name => PipelineStepNames.Parsing;

    public async Task ExecuteAsync(ImportPipelineContext context)
    {
        await processor.ExecuteAsync(context.SourceCode, context.CancellationToken);
    }
}