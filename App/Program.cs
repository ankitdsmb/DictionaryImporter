using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Canonical;
using DictionaryImporter.Core.Graph;
using DictionaryImporter.Core.Linguistics;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Core.PostProcessing;
using DictionaryImporter.Core.Sources;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Infrastructure;
using DictionaryImporter.Infrastructure.Canonical;
using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Infrastructure.Linguistics;
using DictionaryImporter.Infrastructure.Merge;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Infrastructure.PostProcessing;
using DictionaryImporter.Infrastructure.Verification;
using DictionaryImporter.Orchestration;
using DictionaryImporter.Sources.Gutenberg;
using DictionaryImporter.Sources.Gutenberg.Parsing;
using DictionaryImporter.Sources.GutenbergWebster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        // ------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------
        IConfiguration configuration =
            new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

        // ------------------------------------------------------------
        // Logging
        // ------------------------------------------------------------
        Log.Logger =
            new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("logs/dictionary-importer-.log",
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();

        var services = new ServiceCollection();

        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSerilog(dispose: true);
        });

        services.AddSingleton(configuration);

        // ------------------------------------------------------------
        // Connection string (single source of truth)
        // ------------------------------------------------------------
        var connectionString =
            configuration.GetConnectionString("DictionaryImporter")
            ?? throw new InvalidOperationException(
                "Connection string 'DictionaryImporter' not configured");

        // ------------------------------------------------------------
        // Core Infrastructure
        // ------------------------------------------------------------
        services.AddSingleton<IStagingLoader>(sp =>
            new SqlDictionaryEntryStagingLoader(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryStagingLoader>>()));

        services.AddSingleton<IDataLoader, StagingDataLoaderAdapter>();

        services.AddSingleton<IEntryEtymologyWriter>(
            _ => new SqlDictionaryEntryEtymologyWriter(connectionString));

        services.AddSingleton<ICanonicalWordResolver>(sp =>
            new SqlCanonicalWordResolver(
                connectionString,
                sp.GetRequiredService<ILogger<SqlCanonicalWordResolver>>()));

        services.AddSingleton<IDataMergeExecutor>(sp =>
            new SqlDictionaryEntryMergeExecutor(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

        services.AddSingleton<IPostMergeVerifier>(sp =>
            new SqlPostMergeVerifier(
                connectionString,
                sp.GetRequiredService<ILogger<SqlPostMergeVerifier>>()));

        // ------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------
        services.AddTransient<IDictionaryEntryValidator,
            DefaultDictionaryEntryValidator>();

        // ------------------------------------------------------------
        // Linguistics
        // ------------------------------------------------------------
        services.AddSingleton<IPartOfSpeechInfererV2,
            ParsedDefinitionPartOfSpeechInfererV2>();

        services.AddSingleton<DictionaryEntryPartOfSpeechResolver>(sp =>
            new DictionaryEntryPartOfSpeechResolver(
                connectionString,
                sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                sp.GetRequiredService<ILogger<DictionaryEntryPartOfSpeechResolver>>()));

        // ------------------------------------------------------------
        // Definition & Post-Processing
        // ------------------------------------------------------------
        services.AddSingleton<IDictionaryDefinitionParser,
            WebsterSubEntryParser>();

        services.AddSingleton(_ => new SqlParsedDefinitionWriter(connectionString));
        services.AddSingleton(_ => new SqlDictionaryAliasWriter(connectionString));
        services.AddSingleton(_ => new SqlDictionaryEntrySynonymWriter(connectionString));
        services.AddSingleton(_ => new SqlDictionaryEntryCrossReferenceWriter(connectionString));
        services.AddSingleton(_ => new SqlDictionaryCrossReferenceResolvedWriter(connectionString));
        services.AddSingleton(_ => new SqlDictionaryEntryVariantWriter(connectionString));

        services.AddSingleton<DictionaryPostProcessor>(sp =>
            new DictionaryPostProcessor(
                connectionString,
                sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                sp.GetRequiredService<ILogger<DictionaryPostProcessor>>()));

        // ------------------------------------------------------------
        // Graph infrastructure (CONCRETE + INTERFACE)
        // ------------------------------------------------------------

        // Node builder (CONCRETE – required by ImportOrchestrator)
        services.AddSingleton<DictionaryGraphNodeBuilder>(
            _ => new DictionaryGraphNodeBuilder(connectionString));

        // Edge builder (CONCRETE – required by ImportOrchestrator)
        services.AddSingleton<DictionaryGraphBuilder>(
            _ => new DictionaryGraphBuilder(connectionString));

        // Interface mapping (optional, but keeps architecture intact)
        services.AddSingleton<IGraphBuilder>(
            sp => sp.GetRequiredService<DictionaryGraphBuilder>());

        // Validator
        services.AddSingleton<DictionaryGraphValidator>(sp =>
            new DictionaryGraphValidator(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryGraphValidator>>()));

        services.AddSingleton<IGraphValidator>(
            sp => sp.GetRequiredService<DictionaryGraphValidator>());


        // ------------------------------------------------------------
        // Concept & Ranking
        // ------------------------------------------------------------
        services.AddSingleton(_ => new DictionaryConceptBuilder(connectionString));
        services.AddSingleton(_ => new DictionaryConceptMerger(connectionString));
        services.AddSingleton(_ => new DictionaryConceptConfidenceCalculator(connectionString));
        services.AddSingleton(_ => new DictionaryGraphRankCalculator(connectionString));

        // ------------------------------------------------------------
        // Source modules
        // ------------------------------------------------------------
        IDictionarySourceModule[] sourceModules =
        {
            new GutenbergWebsterSourceModule()
        };

        foreach (var module in sourceModules)
            module.RegisterServices(services, configuration);

        services.AddSingleton<IEnumerable<IDictionarySourceModule>>(sourceModules);

        // ------------------------------------------------------------
        // Import engine + factories
        // ------------------------------------------------------------
        services.AddSingleton<ImportEngineFactory<GutenbergRawEntry>>();

        services.AddSingleton<Func<IDictionaryEntryValidator>>(sp =>
            () => sp.GetRequiredService<IDictionaryEntryValidator>());

        services.AddSingleton<Func<IDataMergeExecutor>>(sp =>
            () => sp.GetRequiredService<IDataMergeExecutor>());

        services.AddSingleton<Func<ImportEngineFactory<GutenbergRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<GutenbergRawEntry>>());

        services.AddSingleton<DictionaryEntryLinguisticEnricher>(sp =>
            new DictionaryEntryLinguisticEnricher(
                connectionString,
                sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                sp.GetRequiredService<ILogger<DictionaryEntryLinguisticEnricher>>()
            ));

        services.AddSingleton<DictionaryParsedDefinitionProcessor>(sp =>
            new DictionaryParsedDefinitionProcessor(
                connectionString,
                sp.GetRequiredService<IDictionaryDefinitionParser>(),
                sp.GetRequiredService<SqlParsedDefinitionWriter>(),
                sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()
            ));


        // ------------------------------------------------------------
        // Orchestrator (FINAL)
        // ------------------------------------------------------------
        services.AddSingleton<ImportOrchestrator>();

        // ------------------------------------------------------------
        // Run
        // ------------------------------------------------------------
        using var provider = services.BuildServiceProvider();

        var orchestrator =
            provider.GetRequiredService<ImportOrchestrator>();

        var sources = sourceModules
            .Select(m => m.BuildSource(configuration))
            .ToList();

        await orchestrator.RunAsync(sources, CancellationToken.None);

        Log.CloseAndFlush();
    }
}