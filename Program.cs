using DictionaryImporter.Bootstrap;
using DictionaryImporter.Core.Jobs;
using DictionaryImporter.Core.Orchestration;
using DictionaryImporter.Core.Orchestration.Sources;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var configuration = BootstrapConfiguration.Build();
BootstrapLogging.Configure();

var services = new ServiceCollection();
BootstrapLogging.Register(services);

services.AddSingleton(configuration);

BootstrapInfrastructure.Register(services, configuration);
BootstrapSources.Register(services, configuration);
BootstrapPipeline.Register(services, configuration);

// ✅ Register RuleBasedRewriteJob
services.Configure<RuleBasedRewriteJobOptions>(
    configuration.GetSection("RuleBasedRewriteJob"));
services.AddScoped<RuleBasedRewriteJob>();

using var provider = services.BuildServiceProvider();

var argsList = (args ?? Array.Empty<string>())
    .Select(a => a.Trim())
    .Where(a => !string.IsNullOrWhiteSpace(a))
    .ToArray();

var runImport = argsList.Length == 0 || argsList.Contains("--import", StringComparer.OrdinalIgnoreCase);
var runRewrite = argsList.Contains("--rewrite", StringComparer.OrdinalIgnoreCase);

using var cts = new CancellationTokenSource();
//await luceneIndexBuilder.BuildOrUpdateIndexAsync(indexPath, sourceCode, ct);

try
{
    if (runImport)
    {
        // ✅ FIX: orchestrator is Scoped => resolve inside a scope
        using var scope = provider.CreateScope();

        var orchestrator = scope.ServiceProvider.GetRequiredService<ImportOrchestrator>();
        var pipelineMode = BootstrapPipeline.ResolvePipelineMode(configuration);

        var sources = SourceRegistry.CreateSources()
            .Select(m => m.BuildSource(configuration))
            .ToList();

        await orchestrator.RunAsync(sources, pipelineMode, cts.Token);
    }

    if (runRewrite)
    {
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<RuleBasedRewriteJob>();
        await job.RunAsync(cts.Token);
    }
}
catch (Exception ex)
{
    // ✅ Production-safe: never crash without logs
    Log.Error(ex, "Fatal error in Program.cs execution.");
}
finally
{
    Log.CloseAndFlush();
}
