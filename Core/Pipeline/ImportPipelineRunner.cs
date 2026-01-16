namespace DictionaryImporter.Core.Pipeline;

public sealed class ImportPipelineRunner(
    IEnumerable<IImportPipelineStep> steps,
    ILogger<ImportPipelineRunner> logger)
{
    private readonly IReadOnlyDictionary<string, IImportPipelineStep> _steps =
        steps.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

    public async Task RunAsync(ImportPipelineContext context, IReadOnlyList<string> stepOrder)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (stepOrder == null || stepOrder.Count == 0)
            throw new InvalidOperationException("Pipeline step order is empty.");

        foreach (var stepName in stepOrder)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!_steps.TryGetValue(stepName, out var step))
                throw new InvalidOperationException($"Pipeline step not registered: {stepName}");

            logger.LogInformation("Stage={Stage} started | Code={Code}", stepName, context.SourceCode);

            await step.ExecuteAsync(context);

            logger.LogInformation("Stage={Stage} completed | Code={Code}", stepName, context.SourceCode);
        }
    }
}