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
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Orchestration
{
    public sealed class ImportOrchestrator
    {
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
        private readonly ILogger<ImportOrchestrator> _logger;

        private readonly Func<IDictionaryEntryValidator> _validatorFactory;
        private readonly Func<IDataMergeExecutor> _mergeFactory;
        private readonly Func<ImportEngineFactory<GutenbergRawEntry>> _engineFactory;
        private readonly ICanonicalWordResolver _canonicalResolver;

        private readonly CanonicalWordIpaEnricher _ipaEnricher;
        private readonly IpaVerificationReporter _ipaVerificationReporter;
        public ImportOrchestrator(
            Func<IDictionaryEntryValidator> validatorFactory,
            Func<IDataMergeExecutor> mergeFactory,
            Func<ImportEngineFactory<GutenbergRawEntry>> engineFactory,
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
            IpaVerificationReporter ipaVerificationReporter)
        {
            _validatorFactory = validatorFactory;
            _mergeFactory = mergeFactory;
            _engineFactory = engineFactory;
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
        }

        public async Task RunAsync(
            IEnumerable<ImportSourceDefinition> sources,
            CancellationToken ct)
        {
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "Starting source {Source} ({Code})",
                    source.SourceName,
                    source.SourceCode);

                try
                {
                    using var stream = source.OpenStream();

                    var validator = _validatorFactory();
                    var engine = _engineFactory().Create(validator);

                    // 1. Raw import
                    await engine.ImportAsync(stream, ct);

                    // 2. Merge staging
                    await _mergeFactory().ExecuteAsync(source.SourceCode, ct);

                    // 3. Canonical resolution
                    await _canonicalResolver.ResolveAsync(source.SourceCode, ct);

                    // 4. Definition parsing (SENSES + CROSS-REFERENCES)
                    await _parsedDefinitionProcessor.ExecuteAsync(source.SourceCode, ct);

                    // 5. Linguistic enrichment (POS + SYNONYMS)
                    await _linguisticEnricher.ExecuteAsync(source.SourceCode, ct);

                    // 6. Graph materialization
                    await _graphNodeBuilder.BuildAsync(source.SourceCode, ct);
                    await _graphBuilder.BuildAsync(source.SourceCode, ct);

                    // 7. Graph validation
                    await _graphValidator.ValidateAsync(source.SourceCode, ct);

                    // 8. Concepts
                    await _conceptBuilder.BuildAsync(source.SourceCode, ct);

                    // 9. Global concept processing
                    await _conceptMerger.MergeAsync(ct);

                    await _conceptConfidenceCalculator.CalculateAsync(ct);

                    await _graphRankCalculator.CalculateAsync(ct);

                    await _ipaEnricher.ExecuteAsync(
                    "en-US",
                    @"Data\IPA\en_US.txt",
                    ct);

                    await _ipaEnricher.ExecuteAsync(
                        "en-UK",
                        @"Data\IPA\en_UK.txt",
                        ct);

                    // 10. Final verification
                    await _postMergeVerifier.VerifyAsync(source.SourceCode, ct);

                    await _ipaVerificationReporter.ReportAsync(ct);

                    _logger.LogInformation(
                        "Source completed successfully {Source} ({Code})",
                        source.SourceName,
                        source.SourceCode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Source FAILED {Source} ({Code})",
                        source.SourceName,
                        source.SourceCode);

                    throw;
                }
            }
        }
    }
}