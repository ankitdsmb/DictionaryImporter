using DictionaryImporter.Bootstrap;

var configuration = BootstrapConfiguration.Build();

BootstrapLogging.Configure();

var services = new ServiceCollection();
BootstrapLogging.Register(services);

services.AddSingleton(configuration);

BootstrapInfrastructure.Register(services, configuration);
BootstrapSources.Register(services, configuration);
BootstrapPipeline.Register(services, configuration);

using var provider = services.BuildServiceProvider();

var orchestrator = provider.GetRequiredService<ImportOrchestrator>();

var pipelineMode = BootstrapPipeline.ResolvePipelineMode(configuration);

var sources = SourceRegistry.CreateSources()
    .Select(m => m.BuildSource(configuration))
    .ToList();

await orchestrator.RunAsync(
    sources,
    pipelineMode,
    CancellationToken.None);

Log.CloseAndFlush();