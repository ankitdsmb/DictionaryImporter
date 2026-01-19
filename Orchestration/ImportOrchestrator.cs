using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Orchestration
{
    public sealed class ImportOrchestrator(
        Func<IDictionaryEntryValidator> validatorFactory,
        Func<IDataMergeExecutor> mergeFactory,
        IImportEngineRegistry engineRegistry,
        ICanonicalWordResolver canonicalResolver,
        DictionaryParsedDefinitionProcessor parsedDefinitionProcessor,
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
        ILogger<ImportOrchestrator> logger,
        QaRunner qaRunner)
    {
        public async Task RunAsync(
            IEnumerable<ImportSourceDefinition> sources,
            PipelineMode mode,
            CancellationToken ct)
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();

                logger.LogInformation(
                    "Pipeline started | Source={Source} | Code={Code} | Mode={Mode}",
                    source.SourceName,
                    source.SourceCode,
                    mode);

                try
                {
                    SourceDataHelper.ResetProcessingState(source.SourceCode);

                    // Always run import/merge (and stop early for ImportOnly)
                    await RunImportMergeAsync(source, mode, ct);

                    if (mode == PipelineMode.ImportOnly)
                    {
                        logger.LogInformation(
                            "Pipeline completed (ImportOnly) | Source={Source} | Code={Code}",
                            source.SourceName,
                            source.SourceCode);

                        continue;
                    }

                    // Pipeline steps are config-driven
                    var orderedSteps = pipelineOrderResolver.Resolve(source.SourceCode);

                    // We already did Import + Merge above, so pipeline should start from Canonicalization
                    // But we still validate config here (in case someone includes Import/Merge)
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
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Pipeline FAILED | Source={Source} | Code={Code}",
                        source.SourceName,
                        source.SourceCode);

                    throw;
                }
            }
        }

        private async Task RunImportMergeAsync(
            ImportSourceDefinition source,
            PipelineMode mode,
            CancellationToken ct)
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
        }
    }
}