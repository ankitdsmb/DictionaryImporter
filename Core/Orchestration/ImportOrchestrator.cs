using DictionaryImporter.Common;
using DictionaryImporter.Core.Orchestration.Concurrency;
using DictionaryImporter.Core.Orchestration.Models;
using DictionaryImporter.Core.Orchestration.Pipeline;
using DictionaryImporter.Core.Orchestration.Pipeline.Steps;
using DictionaryImporter.Core.Orchestration.Sources;

namespace DictionaryImporter.Core.Orchestration;

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
    ImportConcurrencyManager concurrencyManager,
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

        var parallelOptions = concurrencyManager.GetParallelOptions(ct);

        logger.LogInformation(
            "Starting parallel import | Sources={SourceCount} | Mode={Mode} | MaxDegreeOfParallelism={MaxDop}",
            sourceList.Count,
            mode,
            parallelOptions.MaxDegreeOfParallelism);

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

        if (_sourceResults.Values.Any(r => !r.Success))
        {
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

            await RunImportMergeAsync(source, mode, ct);

            if (mode == PipelineMode.ImportOnly)
            {
                result.Success = true;
                return result;
            }

            var orderedSteps = pipelineOrderResolver.Resolve(source.SourceCode);

            var safeOrder = orderedSteps
                .Where(x => !string.Equals(x, PipelineStepNames.Import, StringComparison.OrdinalIgnoreCase))
                .Where(x => !string.Equals(x, PipelineStepNames.Merge, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var ctx = new ImportPipelineContext(source, ct);
            await pipelineRunner.RunAsync(ctx, safeOrder);

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
            Max DB Concurrency: {MaxConcurrency}
            Active Sources: {ActiveSources}
            Queue Length: {QueueLength}
            """,
            _metrics.TotalSources,
            _metrics.SuccessfulSources,
            _metrics.FailedSources,
            _metrics.Duration.TotalSeconds,
            avgDuration,
            concurrencyMetrics.MaxConcurrency,
            concurrencyMetrics.ActiveSources,
            concurrencyMetrics.QueueLength);
    }
}