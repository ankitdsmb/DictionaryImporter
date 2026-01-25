namespace DictionaryImporter.Core.Pipeline
{
    public sealed class ImportPipelineRunner(
        IEnumerable<IImportPipelineStep> steps,
        ILogger<ImportPipelineRunner> logger)
    {
        private readonly IReadOnlyDictionary<string, IImportPipelineStep> _steps =
            BuildStepMap(steps, logger);

        public async Task RunAsync(ImportPipelineContext context, IReadOnlyList<string> stepOrder)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (stepOrder == null || stepOrder.Count == 0)
            {
                logger.LogWarning(
                    "Pipeline step order is empty. Source={Source}. Nothing to execute.",
                    context.SourceCode);
                return;
            }

            foreach (var stepNameRaw in stepOrder)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var stepName = stepNameRaw?.Trim();

                if (string.IsNullOrWhiteSpace(stepName))
                    continue;

                if (!_steps.TryGetValue(stepName, out var step))
                {
                    logger.LogError(
                        "Pipeline step not registered: {Step}. Source={Source}. Skipping.",
                        stepName,
                        context.SourceCode);
                    continue;
                }

                logger.LogInformation(
                    "Stage={Stage} started | Code={Code}",
                    stepName,
                    context.SourceCode);

                try
                {
                    await step.ExecuteAsync(context);

                    logger.LogInformation(
                        "Stage={Stage} completed | Code={Code}",
                        stepName,
                        context.SourceCode);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Stage={Stage} failed | Code={Code}. Continuing pipeline.",
                        stepName,
                        context.SourceCode);
                }
            }
        }

        // NEW METHOD (added)
        private static IReadOnlyDictionary<string, IImportPipelineStep> BuildStepMap(
            IEnumerable<IImportPipelineStep> steps,
            ILogger logger)
        {
            var map = new Dictionary<string, IImportPipelineStep>(StringComparer.OrdinalIgnoreCase);

            if (steps == null)
                return map;

            foreach (var step in steps)
            {
                if (step == null)
                    continue;

                if (string.IsNullOrWhiteSpace(step.Name))
                    continue;

                if (map.TryGetValue(step.Name, out var existing))
                {
                    logger.LogWarning(
                        "Duplicate pipeline step registered: {StepName}. Using first instance: {ExistingType}. Ignoring: {NewType}.",
                        step.Name,
                        existing.GetType().Name,
                        step.GetType().Name);

                    continue;
                }

                map[step.Name] = step;
            }

            return map;
        }
    }
}
