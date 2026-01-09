using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Canonical;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Infrastructure.Parsing;
using DictionaryImporter.Infrastructure.PostProcessing;
using DictionaryImporter.Infrastructure.PostProcessing.Enrichment;
using DictionaryImporter.Infrastructure.PostProcessing.Verification;
using DictionaryImporter.Infrastructure.Verification;
using DictionaryImporter.Sources.Gutenberg;
using DictionaryImporter.Sources.StructuredJson;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Orchestration
{
    public sealed class ImportOrchestrator
    {
        private readonly Func<IDictionaryEntryValidator> _validatorFactory;
        private readonly Func<IDataMergeExecutor> _mergeFactory;
        private readonly IImportEngineRegistry _engineRegistry;
        private readonly ILogger<ImportOrchestrator> _logger;

        private readonly DictionaryParsedDefinitionProcessor _parsedDefinitionProcessor;
        private readonly DictionaryEntryLinguisticEnricher _linguisticEnricher;
        private readonly DictionaryGraphNodeBuilder _graphNodeBuilder;
        private readonly DictionaryGraphBuilder _graphBuilder;
        private readonly DictionaryGraphValidator _graphValidator;
        private readonly DictionaryConceptBuilder _conceptBuilder;
        private readonly DictionaryConceptMerger _conceptMerger;
        private readonly DictionaryConceptConfidenceCalculator _conceptConfidenceCalculator;
        private readonly DictionaryGraphRankCalculator _graphRankCalculator;
        private readonly IPostMergeVerifier _postMergeVerifier;

        private readonly ICanonicalWordResolver _canonicalResolver;
        private readonly CanonicalWordIpaEnricher _ipaEnricher;
        private readonly IpaVerificationReporter _ipaVerificationReporter;
        private readonly IReadOnlyList<IpaSourceConfig> _ipaSources;

        public ImportOrchestrator(
            Func<IDictionaryEntryValidator> validatorFactory,
            Func<IDataMergeExecutor> mergeFactory,
            Func<ImportEngineFactory<GutenbergRawEntry>> gutenbergEngineFactory,
            Func<ImportEngineFactory<StructuredJsonRawEntry>> jsonEngineFactory,
            IImportEngineRegistry engineRegistry,
            ICanonicalWordResolver canonicalResolver,
            DictionaryParsedDefinitionProcessor parsedDefinitionProcessor,
            DictionaryEntryLinguisticEnricher linguisticEnricher,
            DictionaryGraphNodeBuilder graphNodeBuilder,
            DictionaryGraphBuilder graphBuilder,
            DictionaryGraphValidator graphValidator,
            DictionaryConceptBuilder conceptBuilder,
            DictionaryConceptMerger conceptMerger,
            DictionaryConceptConfidenceCalculator conceptConfidenceCalculator,
            DictionaryGraphRankCalculator graphRankCalculator,
            IPostMergeVerifier postMergeVerifier,
            ILogger<ImportOrchestrator> logger,
            CanonicalWordIpaEnricher ipaEnricher,
            IpaVerificationReporter ipaVerificationReporter,
            IReadOnlyList<IpaSourceConfig> ipaSources)
        {
            _validatorFactory = validatorFactory;
            _mergeFactory = mergeFactory;
            _engineRegistry = engineRegistry;
            _canonicalResolver = canonicalResolver;
            _parsedDefinitionProcessor = parsedDefinitionProcessor;
            _linguisticEnricher = linguisticEnricher;
            _graphNodeBuilder = graphNodeBuilder;
            _graphBuilder = graphBuilder;
            _graphValidator = graphValidator;
            _conceptBuilder = conceptBuilder;
            _conceptMerger = conceptMerger;
            _conceptConfidenceCalculator = conceptConfidenceCalculator;
            _graphRankCalculator = graphRankCalculator;
            _postMergeVerifier = postMergeVerifier;
            _logger = logger;
            _ipaEnricher = ipaEnricher;
            _ipaVerificationReporter = ipaVerificationReporter;
            _ipaSources = ipaSources;
        }

        public async Task RunAsync(
            IEnumerable<ImportSourceDefinition> sources,
            PipelineMode mode,
            CancellationToken ct)
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "Pipeline started | Source={Source} | Code={Code} | Mode={Mode}",
                    source.SourceName,
                    source.SourceCode,
                    mode);

                try
                {
                    using var stream = source.OpenStream();
                    var validator = _validatorFactory();

                    // 1. IMPORT
                    _logger.LogInformation(
                        "Stage=Import started | Code={Code}",
                        source.SourceCode);

                    var engine =
                        _engineRegistry.CreateEngine(
                            source.SourceCode,
                            validator);

                    await engine.ImportAsync(stream, ct);

                    _logger.LogInformation(
                        "Stage=Import completed | Code={Code}",
                        source.SourceCode);

                    // 2. MERGE
                    _logger.LogInformation(
                        "Stage=Merge started | Code={Code}",
                        source.SourceCode);

                    await _mergeFactory().ExecuteAsync(source.SourceCode, ct);

                    _logger.LogInformation(
                        "Stage=Merge completed | Code={Code}",
                        source.SourceCode);

                    if (mode == PipelineMode.ImportOnly)
                    {
                        _logger.LogInformation(
                            "Pipeline completed (ImportOnly) | Source={Source} | Code={Code}",
                            source.SourceName,
                            source.SourceCode);

                        continue;
                    }

                    // 3. CANONICALIZATION
                    _logger.LogInformation(
                        "Stage=Canonicalization started | Code={Code}",
                        source.SourceCode);

                    await _canonicalResolver.ResolveAsync(source.SourceCode, ct);

                    _logger.LogInformation(
                        "Stage=Canonicalization completed | Code={Code}",
                        source.SourceCode);

                    // 4. PARSING
                    _logger.LogInformation(
                        "Stage=Parsing started | Code={Code}",
                        source.SourceCode);

                    await _parsedDefinitionProcessor.ExecuteAsync(source.SourceCode, ct);

                    _logger.LogInformation(
                        "Stage=Parsing completed | Code={Code}",
                        source.SourceCode);

                    // 5. LINGUISTICS
                    _logger.LogInformation(
                        "Stage=Linguistics started | Code={Code}",
                        source.SourceCode);

                    await _linguisticEnricher.ExecuteAsync(source.SourceCode, ct);

                    _logger.LogInformation(
                        "Stage=Linguistics completed | Code={Code}",
                        source.SourceCode);

                    // 6. GRAPH
                    _logger.LogInformation(
                        "Stage=GraphBuild started | Code={Code}",
                        source.SourceCode);

                    await _graphNodeBuilder.BuildAsync(source.SourceCode, ct);
                    await _graphBuilder.BuildAsync(source.SourceCode, ct);

                    _logger.LogInformation(
                        "Stage=GraphBuild completed | Code={Code}",
                        source.SourceCode);

                    // 7. GRAPH VALIDATION
                    _logger.LogInformation(
                        "Stage=GraphValidation started | Code={Code}",
                        source.SourceCode);

                    await _graphValidator.ValidateAsync(source.SourceCode, ct);

                    _logger.LogInformation(
                        "Stage=GraphValidation completed | Code={Code}",
                        source.SourceCode);

                    // 8. CONCEPTS
                    _logger.LogInformation(
                        "Stage=ConceptBuild started | Code={Code}",
                        source.SourceCode);

                    await _conceptBuilder.BuildAsync(source.SourceCode, ct);

                    _logger.LogInformation(
                        "Stage=ConceptBuild completed | Code={Code}",
                        source.SourceCode);

                    // 9. GLOBAL POST-CONCEPT
                    _logger.LogInformation(
                        "Stage=ConceptMerge started");

                    await _conceptMerger.MergeAsync(ct);
                    await _conceptConfidenceCalculator.CalculateAsync(ct);
                    await _graphRankCalculator.CalculateAsync(ct);

                    _logger.LogInformation(
                        "Stage=ConceptMerge completed");

                    // 10. IPA (config-driven)
                    foreach (var ipa in _ipaSources)
                    {
                        _logger.LogInformation(
                            "Stage=IPA started | Locale={Locale} | Path={Path}",
                            ipa.Locale,
                            ipa.FilePath);

                        await _ipaEnricher.ExecuteAsync(
                            ipa.Locale,
                            ipa.FilePath,
                            ct);

                        _logger.LogInformation(
                            "Stage=IPA completed | Locale={Locale}",
                            ipa.Locale);
                    }

                    // 11. VERIFICATION
                    _logger.LogInformation(
                        "Stage=Verification started | Code={Code}",
                        source.SourceCode);

                    await _postMergeVerifier.VerifyAsync(source.SourceCode, ct);
                    await _ipaVerificationReporter.ReportAsync(ct);

                    _logger.LogInformation(
                        "Stage=Verification completed | Code={Code}",
                        source.SourceCode);

                    _logger.LogInformation(
                        "Pipeline completed successfully | Source={Source} | Code={Code}",
                        source.SourceName,
                        source.SourceCode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Pipeline FAILED | Source={Source} | Code={Code}",
                        source.SourceName,
                        source.SourceCode);

                    throw;
                }
            }
        }
    }
}
