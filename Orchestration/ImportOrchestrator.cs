using DictionaryImporter.Infrastructure.Parsing;
using DictionaryImporter.Infrastructure.PostProcessing;
using DictionaryImporter.Infrastructure.PostProcessing.Verification;
using DictionaryImporter.Infrastructure.Qa;
using DictionaryImporter.Infrastructure.Verification;

namespace DictionaryImporter.Orchestration;

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
    GrammarCorrectionStep grammarCorrectionStep,
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
                await using var stream = source.OpenStream();
                var validator = validatorFactory();

                logger.LogInformation("Stage=Import started | Code={Code}", source.SourceCode);
                var engine = engineRegistry.CreateEngine(source.SourceCode, validator);
                await engine.ImportAsync(stream, ct);
                logger.LogInformation("Stage=Import completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=Merge started | Code={Code}", source.SourceCode);
                await mergeFactory().ExecuteAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=Merge completed | Code={Code}", source.SourceCode);

                if (mode == PipelineMode.ImportOnly)
                    continue;

                logger.LogInformation("Stage=Canonicalization started | Code={Code}", source.SourceCode);
                await canonicalResolver.ResolveAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=Canonicalization completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=Parsing started | Code={Code}", source.SourceCode);
                await parsedDefinitionProcessor.ExecuteAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=Parsing completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=Linguistics started | Code={Code}", source.SourceCode);
                await linguisticEnricher.ExecuteAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=Linguistics completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=GrammarCorrection started | Code={Code}", source.SourceCode);
                await grammarCorrectionStep.ExecuteAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=GrammarCorrection completed | Code={Code}", source.SourceCode);

                foreach (var ipa in ipaSources)
                {
                    logger.LogInformation(
                        "Stage=OrthographicSyllables started | Locale={Locale}",
                        ipa.Locale);

                    await orthographicSyllableEnricher.ExecuteAsync(
                        ipa.Locale,
                        ct);

                    logger.LogInformation(
                        "Stage=OrthographicSyllables completed | Locale={Locale}",
                        ipa.Locale);
                }

                logger.LogInformation("Stage=GraphBuild started | Code={Code}", source.SourceCode);
                await graphNodeBuilder.BuildAsync(source.SourceCode, ct);
                await graphBuilder.BuildAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=GraphBuild completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=GraphValidation started | Code={Code}", source.SourceCode);
                await graphValidator.ValidateAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=GraphValidation completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=ConceptBuild started | Code={Code}", source.SourceCode);
                await conceptBuilder.BuildAsync(source.SourceCode, ct);
                logger.LogInformation("Stage=ConceptBuild completed | Code={Code}", source.SourceCode);

                logger.LogInformation("Stage=ConceptMerge started");
                await conceptMerger.MergeAsync(ct);
                await conceptConfidenceCalculator.CalculateAsync(ct);
                await graphRankCalculator.CalculateAsync(ct);
                logger.LogInformation("Stage=ConceptMerge completed");

                foreach (var ipa in ipaSources)
                {
                    logger.LogInformation(
                        "Stage=IPA started | Locale={Locale} | Path={Path}",
                        ipa.Locale,
                        ipa.FilePath);

                    await ipaEnricher.ExecuteAsync(
                        ipa.Locale,
                        ipa.FilePath,
                        ct);

                    logger.LogInformation(
                        "Stage=IPA completed | Locale={Locale}",
                        ipa.Locale);
                }

                logger.LogInformation("Stage=IpaSyllables started");
                await syllableEnricher.ExecuteAsync(ct);
                logger.LogInformation("Stage=IpaSyllables completed");

                logger.LogInformation("Stage=Verification started | Code={Code}", source.SourceCode);
                await postMergeVerifier.VerifyAsync(source.SourceCode, ct);
                await ipaVerificationReporter.ReportAsync(ct);
                logger.LogInformation("Stage=Verification completed | Code={Code}", source.SourceCode);

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
}