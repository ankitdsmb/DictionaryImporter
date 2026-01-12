using DictionaryImporter.Bootstrap;
using Serilog;

// Configuration
var configuration = BootstrapConfiguration.Build();

// Logging
BootstrapLogging.Configure();

var services = new ServiceCollection();
BootstrapLogging.Register(services);

services.AddSingleton(configuration);

// Dependency Injection
BootstrapInfrastructure.Register(services, configuration);
BootstrapSources.Register(services, configuration);
BootstrapPipeline.Register(services, configuration);

// Run
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