using DictionaryImporter.Bootstrap;
using DictionaryImporter.Validation;
using Serilog;

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

EncodingAwareValidation.Run();

// ✅ FIX: prevent “startup stuck forever” when DB query / pipeline step blocks
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
await orchestrator.RunAsync(sources, pipelineMode, cts.Token);

Log.CloseAndFlush();