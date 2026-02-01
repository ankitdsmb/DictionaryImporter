using DictionaryImporter.Bootstrap;
using DictionaryImporter.Core.Jobs;
using DictionaryImporter.Core.Orchestration;
using DictionaryImporter.Core.Orchestration.Sources;
using DictionaryImporter.Gateway.Grammar.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var configuration = BootstrapConfiguration.Build();
BootstrapLogging.Configure();

var connectionString =
    configuration.GetConnectionString("DictionaryImporter")
    ?? throw new InvalidOperationException(
        "Connection string 'DictionaryImporter' not configured");

var services = new ServiceCollection();
BootstrapLogging.Register(services);

services.AddSingleton(configuration);

// Infrastructure
BootstrapInfrastructure.Register(services, configuration);
BootstrapSources.Register(services, configuration);
BootstrapPipeline.Register(services, configuration);

// ✅ Grammar startup cleanup — SINGLE registration
services.AddScoped<GrammarStartupCleanup>(sp =>
    new GrammarStartupCleanup(
        connectionString,
        sp.GetRequiredService<ILogger<GrammarStartupCleanup>>()));

// Rewrite job
services.Configure<RuleBasedRewriteJobOptions>(
    configuration.GetSection("RuleBasedRewriteJob"));
services.AddScoped<RuleBasedRewriteJob>();

using var provider = services.BuildServiceProvider();

using var cts = new CancellationTokenSource();

try
{
    // ✅ 1. STARTUP CLEANUP (FIRST)
    using (var scope = provider.CreateScope())
    {
        var cleanup = scope.ServiceProvider.GetRequiredService<GrammarStartupCleanup>();
        await cleanup.ExecuteAsync(cts.Token);
    }

    var argsList = (args ?? Array.Empty<string>())
        .Select(a => a.Trim())
        .Where(a => !string.IsNullOrWhiteSpace(a))
        .ToArray();

    var runImport = argsList.Length == 0 ||
                    argsList.Contains("--import", StringComparer.OrdinalIgnoreCase);
    var runRewrite = argsList.Contains("--rewrite", StringComparer.OrdinalIgnoreCase);

    // ✅ 2. IMPORT
    if (runImport)
    {
        using var scope = provider.CreateScope();

        var orchestrator = scope.ServiceProvider.GetRequiredService<ImportOrchestrator>();
        var pipelineMode = BootstrapPipeline.ResolvePipelineMode(configuration);

        var sources = SourceRegistry.CreateSources()
            .Select(m => m.BuildSource(configuration))
            .ToList();

        await orchestrator.RunAsync(sources, pipelineMode, cts.Token);
    }

    // ✅ 3. REWRITE
    if (runRewrite)
    {
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<RuleBasedRewriteJob>();
        await job.RunAsync(cts.Token);
    }
}
catch (Exception ex)
{
    Log.Error(ex, "Fatal error in Program.cs execution.");
}
finally
{
    Log.CloseAndFlush();
}