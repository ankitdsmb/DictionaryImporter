using DictionaryImporter.Common;
using System.Collections.Concurrent;

namespace DictionaryImporter.Orchestration;

public sealed class ImportOrchestrator(
    Func<IDictionaryEntryValidator> validatorFactory,
    Func<IDataMergeExecutor> mergeFactory,
    IImportEngineRegistry engineRegistry,
    ICanonicalWordResolver canonicalResolver,
    IParsedDefinitionProcessor parsedDefinitionProcessor,
    DictionaryEntryLinguisticEnricher linguisticEnricher,
    CanonicalWordOrthographicSyllableEnricher orthographicSyllableEnricher,
    DictionaryGraphNodeBuilder graphNodeBuilder,
    DictionaryGraphBuilder graphBuilder,
    DictionaryGraphValidator graphValidator,
    DictionaryConceptBuilder conceptBuilder,
    DictionaryConceptMerger conceptMerger,
    DictionaryConceptConfidenceCalculator conceptConfidenceCalculator,
    DictionaryGraphRankCalculator graphRankCalculator,
    IPostMergeVerifier postMergeVerifier,
    CanonicalWordIpaEnricher ipaEnricher,
    CanonicalWordSyllableEnricher syllableEnricher,
    IpaVerificationReporter ipaVerificationReporter,
    IReadOnlyList<IpaSourceConfig> ipaSources,
    AiEnhancementStep aiEnhancementStep,
    ImportPipelineRunner pipelineRunner,
    ImportPipelineOrderResolver pipelineOrderResolver,
    ImportConcurrencyManager concurrencyManager, // ADDED
    ILogger<ImportOrchestrator> logger,
    QaRunner qaRunner) : IImportOrchestrator
{
    private readonly IParsedDefinitionProcessor _parsedDefinitionProcessor = parsedDefinitionProcessor;
    private readonly ConcurrentDictionary<string, ImportResult> _sourceResults = new();
    private readonly object _metricsLock = new();
    private ImportMetrics _metrics = new();

    public async Task RunAsync(
        IEnumerable<ImportSourceDefinition> sources,
        PipelineMode mode,
        CancellationToken ct)
    {
        var sourceList = sources.ToList();
        _metrics = new ImportMetrics
        {
            TotalSources = sourceList.Count,
            StartTime = DateTime.UtcNow,
            Mode = mode
        };

        logger.LogInformation(
            "Starting parallel import | Sources={SourceCount} | Mode={Mode} | MaxConcurrency=2",
            sourceList.Count,
            mode);

        // Process sources in parallel with max 2 concurrent
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 2,
            TaskScheduler = TaskScheduler.Default
        };

        var parallelTasks = new List<Task>();

        // Use Parallel.ForEachAsync for better resource management
        await Parallel.ForEachAsync(
            sourceList,
            parallelOptions,
            async (source, token) =>
            {
                try
                {
                    var result = await ProcessSingleSourceWithMetricsAsync(source, mode, token);
                    _sourceResults[source.SourceCode] = result;

                    lock (_metricsLock)
                    {
                        _metrics.CompletedSources++;
                        if (result.Success)
                            _metrics.SuccessfulSources++;
                        else
                            _metrics.FailedSources++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "Source processing failed | Source={Source} | Code={Code}",
                        source.SourceName,
                        source.SourceCode);

                    lock (_metricsLock)
                    {
                        _metrics.FailedSources++;
                    }

                    throw;
                }
            });

        _metrics.EndTime = DateTime.UtcNow;
        _metrics.Duration = _metrics.EndTime - _metrics.StartTime;

        LogSummary();

        // Verify all sources completed successfully
        if (_sourceResults.Values.Any(r => !r.Success))
        {
            var failedSources = _sourceResults.Where(kvp => !kvp.Value.Success)
                .Select(kvp => kvp.Key)
                .ToList();

            throw new AggregateException(
                "One or more sources failed to import",
                _sourceResults.Values
                    .Where(r => r.Exception != null)
                    .Select(r => r.Exception!)
                    .ToArray());
        }
    }

    private async Task<ImportResult> ProcessSingleSourceWithMetricsAsync(
        ImportSourceDefinition source,
        PipelineMode mode,
        CancellationToken ct)
    {
        var result = new ImportResult
        {
            SourceCode = source.SourceCode,
            SourceName = source.SourceName,
            StartTime = DateTime.UtcNow
        };

        try
        {
            logger.LogInformation(
                "Pipeline started | Source={Source} | Code={Code} | Mode={Mode}",
                source.SourceName,
                source.SourceCode,
                mode);

            Helper.ResetProcessingState(source.SourceCode);

            // Always run import/merge (and stop early for ImportOnly)
            await RunImportMergeAsync(source, mode, ct);

            if (mode == PipelineMode.ImportOnly)
            {
                logger.LogInformation(
                    "Pipeline completed (ImportOnly) | Source={Source} | Code={Code}",
                    source.SourceName,
                    source.SourceCode);

                result.Success = true;
                result.EndTime = DateTime.UtcNow;
                return result;
            }

            // Pipeline steps are config-driven
            var orderedSteps = pipelineOrderResolver.Resolve(source.SourceCode);

            // Skip Import/Merge steps
            var safeOrder = orderedSteps
                .Where(x => !string.Equals(x, PipelineStepNames.Import, StringComparison.OrdinalIgnoreCase))
                .Where(x => !string.Equals(x, PipelineStepNames.Merge, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var ctx = new ImportPipelineContext(source, ct);
            await pipelineRunner.RunAsync(ctx, safeOrder);

            logger.LogInformation(
                "Pipeline completed successfully | Source={Source} | Code={Code}",
                source.SourceName,
                source.SourceCode);

            result.Success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Pipeline FAILED | Source={Source} | Code={Code}",
                source.SourceName,
                source.SourceCode);

            result.Success = false;
            result.Exception = ex;

            throw;
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
        }

        return result;
    }

    private async Task RunImportMergeAsync(
        ImportSourceDefinition source,
        PipelineMode mode,
        CancellationToken ct)
    {
        await concurrencyManager.ExecuteWithConcurrencyControl(
            source.SourceCode,
            async () =>
            {
                await using var stream = source.OpenStream();
                var validator = validatorFactory();

                logger.LogInformation("Stage=Import started | Code={Code}", source.SourceCode);
                var engine = engineRegistry.CreateEngine(source.SourceCode, validator);
                await engine.ImportAsync(stream, ct);
                logger.LogInformation("Stage=Import completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=Merge started | Code={Code}", source.SourceCode);
                await mergeFactory().ExecuteAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=Merge completed | Code={Code}", source.SourceCode);
            },
            ct);
    }

    private void LogSummary()
    {
        var avgDuration = _sourceResults.Values
            .Where(r => r.Success)
            .Select(r => r.Duration.TotalMilliseconds)
            .DefaultIfEmpty(0)
            .Average();

        var concurrencyMetrics = concurrencyManager.GetMetrics();

        logger.LogInformation(
            """
            Import Summary:
            Total Sources: {TotalSources}
            Successful: {Successful}
            Failed: {Failed}
            Total Duration: {TotalDuration:F2}s
            Average Per Source: {AvgDuration:F2}ms
            Max Concurrency: {MaxConcurrency}
            Avg Queue Length: {AvgQueueLength:F2}
            """,
            _metrics.TotalSources,
            _metrics.SuccessfulSources,
            _metrics.FailedSources,
            _metrics.Duration.TotalSeconds,
            avgDuration,
            concurrencyMetrics.MaxConcurrency,
            concurrencyMetrics.QueueLength);
    }

    public ImportMetrics GetMetrics() => _metrics;

    public IReadOnlyDictionary<string, ImportResult> GetSourceResults() => _sourceResults;
}