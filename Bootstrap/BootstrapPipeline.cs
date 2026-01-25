using DictionaryImporter.Core.Abstractions.Persistence;
using DictionaryImporter.Core.Pipeline.Steps;
using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Domain.Rewrite;
using DictionaryImporter.Gateway.Rewriter;
using DictionaryImporter.Infrastructure.OneTimeTasks;
using DictionaryImporter.Infrastructure.Validation;
using DictionaryImporter.Sources.Century21.Parsing;
using DictionaryImporter.Sources.Collins.parsing;
using DictionaryImporter.Sources.EnglishChinese.Parsing;
using DictionaryImporter.Sources.Kaikki.Parsing;
using DictionaryImporter.Sources.Oxford.Extractor;
using DictionaryImporter.Sources.Oxford.Parsing;
using DictionaryImporter.Sources.Parsing;
using DictionaryImporter.Sources.StructuredJson.Parsing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DictionaryImporter.Bootstrap
{
    public static class BootstrapPipeline
    {
        public static void Register(IServiceCollection services, IConfiguration configuration)
        {
            var connectionString =
                configuration.GetConnectionString("DictionaryImporter")
                ?? throw new InvalidOperationException(
                    "Connection string 'DictionaryImporter' not configured");

            // ------------------------------------------------------------
            // ✅ REQUIRED: GenericSqlBatcher (used by multiple writers)
            // ------------------------------------------------------------

            services.AddSingleton<GenericSqlBatcher>(sp =>
                new GenericSqlBatcher(
                    connectionString,
                    sp.GetRequiredService<ILogger<GenericSqlBatcher>>()));

            // ------------------------------------------------------------
            // ✅ Options Bindings
            // ------------------------------------------------------------

            services.Configure<RuleBasedRewriteExamplesOptions>(
                configuration.GetSection("RuleBasedRewrite:RewriteExamples"));

            services.Configure<RuleBasedRewriteDefinitionsOptions>(
                configuration.GetSection("RuleBasedRewrite:RewriteDefinitions"));

            services.Configure<LuceneSuggestionsOptions>(
                configuration.GetSection("LuceneSuggestions"));

            // ------------------------------------------------------------
            // ✅ Lucene Suggestions Core Services
            // ------------------------------------------------------------

            services.AddSingleton<ILuceneSuggestionIndexRepository>(sp =>
                new SqlLuceneSuggestionIndexRepository(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlLuceneSuggestionIndexRepository>>()));

            services.AddSingleton<ILuceneSuggestionEngine>(sp =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();

                var indexPath = cfg["LuceneSuggestions:IndexPath"];
                if (string.IsNullOrWhiteSpace(indexPath))
                    indexPath = "indexes/lucene/dictionary-rewrite-memory";

                return new LuceneSuggestionEngine(
                    indexPath,
                    sp.GetRequiredService<ILogger<LuceneSuggestionEngine>>());
            });

            services.AddSingleton<LuceneIndexBuilder>();

            // ------------------------------------------------------------
            // ✅ Promotion / Candidate / Rule Hit Tracking Services
            // ------------------------------------------------------------

            services.AddSingleton<IRewriteMapCandidateRepository>(sp =>
                new SqlRewriteMapCandidateRepository(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlRewriteMapCandidateRepository>>()));

            services.AddSingleton<RewriteMapPromotionService>(sp =>
                new RewriteMapPromotionService(
                    connectionString,
                    sp.GetRequiredService<IRewriteMapCandidateRepository>(),
                    sp.GetRequiredService<ILogger<RewriteMapPromotionService>>()));

            services.AddSingleton<IRewriteRuleHitRepository>(sp =>
                new SqlRewriteRuleHitRepository(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlRewriteRuleHitRepository>>()));

            services.AddScoped<RewriteRuleHitBuffer>();

            // ------------------------------------------------------------
            // ✅ FACTORY DELEGATE REGISTRATIONS (keep these)
            // ------------------------------------------------------------

            services.AddSingleton<Func<ImportEngineFactory<KaikkiRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<KaikkiRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<GutenbergRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<GutenbergRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<StructuredJsonRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<StructuredJsonRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<EnglishChineseRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<EnglishChineseRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<CollinsRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<CollinsRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<OxfordRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<OxfordRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<Century21RawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<Century21RawEntry>>);

            services.AddSingleton<IImportEngineRegistry, ImportEngineRegistry>();

            // ------------------------------------------------------------
            // ✅ EXTRACTOR REGISTRATIONS (keep these)
            // ------------------------------------------------------------

            services.AddSingleton<IEtymologyExtractor, OxfordEtymologyExtractor>();
            services.AddSingleton<IExampleExtractor, OxfordExampleExtractor>();
            services.AddSingleton<ISynonymExtractor, OxfordSynonymExtractor>();

            // ------------------------------------------------------------
            // ✅ KEEP: Register default validator (for fallback)
            // ------------------------------------------------------------

            services.AddSingleton<IDictionaryEntryValidator, DefaultDictionaryEntryValidator>();
            services.AddSingleton<Func<IDictionaryEntryValidator>>(sp => () => sp.GetRequiredService<IDictionaryEntryValidator>());

            services.AddSingleton<Func<IDataMergeExecutor>>(sp => sp.GetRequiredService<IDataMergeExecutor>);

            services.AddSingleton<IDataMergeExecutor>(sp => new SqlDictionaryEntryMergeExecutor(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

            // ------------------------------------------------------------
            // ✅ POS REPOSITORY (ONLY ONCE)
            // ------------------------------------------------------------

            services.AddSingleton<IDictionaryEntryPartOfSpeechRepository>(sp =>
                new SqlDictionaryEntryPartOfSpeechRepository(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryPartOfSpeechRepository>>()));

            services.AddSingleton<CanonicalWordIpaEnricher>(sp => new CanonicalWordIpaEnricher(
                connectionString,
                sp.GetRequiredService<SqlCanonicalWordPronunciationWriter>(),
                sp.GetRequiredService<ILogger<CanonicalWordIpaEnricher>>()));

            services.AddSingleton<CanonicalWordSyllableEnricher>(sp => new CanonicalWordSyllableEnricher(
                connectionString,
                sp.GetRequiredService<ILogger<CanonicalWordSyllableEnricher>>()));

            services.AddSingleton<IpaVerificationReporter>(sp => new IpaVerificationReporter(
                connectionString,
                sp.GetRequiredService<ILogger<IpaVerificationReporter>>()));

            services.AddSingleton<OrthographicSyllableRuleResolver>();

            services.AddSingleton<CanonicalWordOrthographicSyllableEnricher>(sp => new CanonicalWordOrthographicSyllableEnricher(
                connectionString,
                sp.GetRequiredService<ILogger<CanonicalWordOrthographicSyllableEnricher>>()));

            services.AddSingleton<IOneTimeDatabaseTask>(new EditorialIpaMigrationTask(connectionString));
            services.AddSingleton<IOneTimeDatabaseTask>(new PromoteIpaFromNotesTask(connectionString));

            // ------------------------------------------------------------
            // ✅ PARSER REGISTRATIONS (keep these)
            // ------------------------------------------------------------

            services.AddSingleton<ISourceDictionaryDefinitionParser, KaikkiDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, GutenbergDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, Century21DefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, CollinsDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, EnglishChineseParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, OxfordDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, StructuredJsonDefinitionParser>();

            // ✅ RESOLVER REGISTRATION (keep this)
            services.AddSingleton<IDictionaryDefinitionParserResolver, DictionaryDefinitionParserResolver>();

            services.AddSingleton<OneTimeTaskRunner>();

            // ------------------------------------------------------------
            // ✅ Used by AI Enhancement step (and RuleBased rewrite job)
            // ------------------------------------------------------------

            services.AddSingleton<IAiAnnotationRepository>(sp =>
                new SqlAiAnnotationRepository(
                    sp.GetRequiredService<IConfiguration>().GetConnectionString("DictionaryImporter")!,
                    sp.GetRequiredService<ILogger<SqlAiAnnotationRepository>>()));

            services.AddScoped<AiEnhancementStep>();

            // ------------------------------------------------------------
            // ✅ Import Pipeline Core
            // ------------------------------------------------------------

            services.Configure<ImportPipelineOptions>(configuration.GetSection("ImportPipeline"));
            services.AddScoped<ImportPipelineOrderResolver>();
            services.AddScoped<ImportPipelineRunner>();

            // ------------------------------------------------------------
            // ✅ PIPELINE STEP REGISTRATIONS (ORDER MATTERS)
            //
            // Correct rewrite order (deterministic):
            // GrammarCorrection -> RuleBasedRewrite -> LuceneSuggestions -> ExampleRewrite -> AiEnhancement
            // ------------------------------------------------------------
            services.AddSingleton<IRewriteMapRepository>(sp =>
                new SqlRewriteMapRepository(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlRewriteMapRepository>>()));

            services.Configure<RewriteMapEngineOptions>(
                configuration.GetSection("RewriteMapEngine"));

            services.AddSingleton<RewriteMapEngine>(sp =>
                new RewriteMapEngine(
                    sp.GetRequiredService<IRewriteMapRepository>(),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RewriteMapEngineOptions>>(),
                    sp.GetRequiredService<ILogger<RewriteMapEngine>>(),
                    sp.GetRequiredService<IRewriteRuleHitRepository>()));
 
            services.AddScoped<IImportPipelineStep, CanonicalizationPipelineStep>();
            services.AddScoped<IImportPipelineStep, ParsingPipelineStep>();
            services.AddScoped<IImportPipelineStep, LinguisticsPipelineStep>();
            services.AddScoped<IImportPipelineStep, GrammarCorrectionPipelineStep>();

            // ✅ RuleBased rewrite services (step registered ONCE)
            services.TryAddScoped<RuleBasedDefinitionEnhancementStep>();
            services.TryAddScoped<RuleBasedRewritePipelineStep>();
            services.AddScoped<IImportPipelineStep>(sp => sp.GetRequiredService<RuleBasedRewritePipelineStep>());

            // ✅ Lucene suggestions step (scoped + added ONCE)
            services.TryAddScoped<LuceneMemorySuggestionsPipelineStep>();
            services.AddScoped<IImportPipelineStep>(sp => sp.GetRequiredService<LuceneMemorySuggestionsPipelineStep>());

            // ✅ Example rewrite repository
            services.AddSingleton<IExampleAiEnhancementRepository>(sp =>
                new SqlExampleAiEnhancementRepository(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlExampleAiEnhancementRepository>>()));

            // ✅ Example rewrite step (scoped + added ONCE)
            services.TryAddScoped<RuleBasedExampleRewritePipelineStep>();
            services.AddScoped<IImportPipelineStep>(sp => sp.GetRequiredService<RuleBasedExampleRewritePipelineStep>());

            // ✅ AI Enhancement step should run AFTER RuleBased rewrite
            services.AddScoped<IImportPipelineStep, AiEnhancementPipelineStep>();

            services.AddScoped<IImportPipelineStep, OrthographicSyllablesPipelineStep>();
            services.AddScoped<IImportPipelineStep, GraphBuildPipelineStep>();
            services.AddScoped<IImportPipelineStep, GraphValidationPipelineStep>();
            services.AddScoped<IImportPipelineStep, ConceptBuildPipelineStep>();
            services.AddScoped<IImportPipelineStep, ConceptMergePipelineStep>();
            services.AddScoped<IImportPipelineStep, IpaPipelineStep>();
            services.AddScoped<IImportPipelineStep, IpaSyllablesPipelineStep>();
            services.AddScoped<IImportPipelineStep, VerificationPipelineStep>();

            // ✅ FIX: DictionaryParsedDefinitionProcessor requires connectionString (string), DI can't resolve string automatically
            services.AddScoped<DictionaryParsedDefinitionProcessor>(sp =>
                ActivatorUtilities.CreateInstance<DictionaryParsedDefinitionProcessor>(sp, connectionString));

            // ✅ Register processor via interface (ONLY ONCE)
            services.AddScoped<IParsedDefinitionProcessor>(sp =>
                sp.GetRequiredService<DictionaryParsedDefinitionProcessor>());

            // ✅ FIX: Required by ExampleExtractorRegistry
            services.AddTransient<DictionaryImporter.Sources.Generic.GenericExampleExtractor>();

            foreach (var qa in KnownQaChecks.CreateAll(connectionString))
                services.AddSingleton<IQaCheck>(qa);

            services.AddSingleton<QaRunner>();
            services.AddScoped<ImportOrchestrator>();

            // ------------------------------------------------------------
            // ✅ Writers using batcher
            // ------------------------------------------------------------

            services.AddTransient<IDictionaryEntryExampleWriter>(sp =>
            {
                var log = sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlDictionaryEntryExampleWriter(connectionString, batcher, log);
            });

            services.AddTransient<SqlParsedDefinitionWriter>(sp =>
            {
                var log = sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlParsedDefinitionWriter(connectionString, batcher, log);
            });

            // ✅ Etymology writer
            services.AddSingleton<IEntryEtymologyWriter>(sp =>
                new SqlDictionaryEntryEtymologyWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

            // ✅ Variant writer
            services.AddSingleton<IDictionaryEntryVariantWriter>(sp =>
                new SqlDictionaryEntryVariantWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryVariantWriter>>()));

            services.AddSingleton<SqlDictionaryEntryVariantWriter>(sp =>
                new SqlDictionaryEntryVariantWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryVariantWriter>>()));
        }

        public static PipelineMode ResolvePipelineMode(IConfiguration configuration)
        {
            return configuration["Pipeline:Mode"] == "ImportOnly"
                ? PipelineMode.ImportOnly
                : PipelineMode.Full;
        }
    }
}
