using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Infrastructure.Implementations;
using DictionaryImporter.AI.Infrastructure.Telemetry;
using DictionaryImporter.AI.Orchestration;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Polly.Extensions.Http;

namespace DictionaryImporter.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiOrchestrationWithInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AiOrchestrationConfiguration> configure = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        var aiConfig = LoadConfiguration(configuration, configure);
        services.AddSingleton(aiConfig);

        var dbConfig = configuration.GetSection("Database").Get<DatabaseConfiguration>();
        services.AddSingleton(dbConfig);

        var cacheConfig = configuration.GetSection("Cache").Get<CacheConfiguration>();
        services.AddSingleton(cacheConfig);

        var telemetryConfig = configuration.GetSection("Telemetry").Get<TelemetryConfiguration>();
        services.AddSingleton(telemetryConfig);

        var securityConfig = configuration.GetSection("Security").Get<SecurityConfiguration>();
        services.AddSingleton(securityConfig);

        RegisterInfrastructureServices(services, aiConfig);

        RegisterHttpClients(services, aiConfig);

        RegisterProviders(services);

        services.AddSingleton<ICompletionOrchestrator, IntelligentOrchestrator>();

        RegisterHealthChecks(services);

        return services;
    }

    private static AiOrchestrationConfiguration LoadConfiguration(
        IConfiguration configuration,
        Action<AiOrchestrationConfiguration> configure)
    {
        var aiConfig = new AiOrchestrationConfiguration();

        configuration.GetSection("AI:Orchestration").Bind(aiConfig);

        var providersSection = configuration.GetSection("AI:Providers");
        if (providersSection.Exists())
        {
            var providers = providersSection.Get<Dictionary<string, ProviderConfiguration>>();
            if (providers != null)
            {
                foreach (var kvp in providers)
                {
                    kvp.Value.Name = kvp.Key;
                    aiConfig.Providers[kvp.Key] = kvp.Value;
                }
            }
        }

        configure?.Invoke(aiConfig);

        return aiConfig;
    }

    private static void RegisterInfrastructureServices(
        IServiceCollection services,
        AiOrchestrationConfiguration aiConfig)
    {
        services.AddSingleton<SqlConnectionFactory, SqlConnectionFactory>();

        if (aiConfig.EnableQuotaManagement)
        {
            services.AddSingleton<IQuotaManager, SqlQuotaManager>();
        }
        else
        {
            services.AddSingleton<IQuotaManager, NullQuotaManager>();
        }

        if (aiConfig.EnableAuditLogging)
        {
            services.AddSingleton<IAuditLogger, SqlAuditLogger>();
        }
        else
        {
            services.AddSingleton<IAuditLogger, NullAuditLogger>();
        }

        if (aiConfig.EnableCaching)
        {
            services.AddSingleton<IResponseCache, DistributedResponseCache>();
        }
        else
        {
            services.AddSingleton<IResponseCache, NullResponseCache>();
        }

        if (aiConfig.EnableMetricsCollection)
        {
            services.AddSingleton<IPerformanceMetricsCollector, SqlMetricsCollector>();
        }
        else
        {
            services.AddSingleton<IPerformanceMetricsCollector, NullMetricsCollector>();
        }

        services.AddSingleton<IApiKeyManager, ConfigurationApiKeyManager>();

        services.AddSingleton<IProviderFactory, EnhancedProviderFactory>();

        if (aiConfig.EnableMetricsCollection)
        {
            services.AddSingleton<ITelemetryService, ApplicationInsightsTelemetry>();
        }
        else
        {
            services.AddSingleton<ITelemetryService, NullTelemetryService>();
        }
    }

    private static void RegisterHttpClients(
        IServiceCollection services,
        AiOrchestrationConfiguration aiConfig)
    {
        foreach (var providerConfig in aiConfig.Providers.Values.Where(p => p.IsEnabled))
        {
            services.AddHttpClient(providerConfig.Name, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(providerConfig.TimeoutSeconds + 5);
                client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");

                if (!string.IsNullOrEmpty(providerConfig.BaseUrl))
                {
                    try
                    {
                        client.BaseAddress = new Uri(providerConfig.BaseUrl);
                    }
                    catch (UriFormatException)
                    {
                    }
                }
            })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                           System.Net.DecompressionMethods.Deflate,
                    UseProxy = true,
                    Proxy = null,
                    UseCookies = false,
                    MaxConnectionsPerServer = 100
                })
                .AddPolicyHandler((serviceProvider, request) =>
                {
                    var logger = serviceProvider.GetRequiredService<ILogger<HttpClient>>();

                    return HttpPolicyExtensions
                        .HandleTransientHttpError()
                        .OrResult(msg => (int)msg.StatusCode >= 500)
                        .WaitAndRetryAsync(
                            providerConfig.MaxRetries,
                            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            onRetry: (outcome, timespan, retryCount, context) =>
                            {
                                logger.LogWarning(
                                    "Retry {RetryCount}/{MaxRetries} for {Provider}. Waiting {Delay}ms. Status: {Status}",
                                    retryCount, providerConfig.MaxRetries, providerConfig.Name,
                                    timespan.TotalMilliseconds,
                                    outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message);
                            });
                })
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(
                    TimeSpan.FromSeconds(providerConfig.TimeoutSeconds)));
        }
    }

    private static void RegisterProviders(IServiceCollection services)
    {
        var providerTypes = new[]
        {
            typeof(OpenRouterProvider),
            typeof(AnthropicProvider),
            typeof(GeminiProvider),
            typeof(TogetherAiProvider),
            typeof(CohereProvider),
            typeof(Ai21Provider),
            typeof(TextCortexProvider),
            typeof(PerplexityProvider),
            typeof(NlpCloudProvider),
            typeof(HuggingFaceProvider),
            typeof(DeepAiProvider),
            typeof(WatsonProvider),
            typeof(AlephAlphaProvider),
            typeof(AlephAlphaVisionProvider),
            typeof(ReplicateProvider),
            typeof(OllamaProvider),
            typeof(StabilityAiProvider),
            typeof(ElevenLabsProvider),
            typeof(AssemblyAiProvider)
        };

        foreach (var providerType in providerTypes)
        {
            services.TryAddTransient(providerType);
        }
    }

    private static void RegisterHealthChecks(IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<AiOrchestrationHealthCheck>(
                "ai_orchestration",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ai", "orchestration", "providers" });

        services.AddHealthChecks()
            .AddMemoryHealthCheck(
                name: "memory",
                maximumMemoryBytes: 1024 * 1024 * 1024, failureStatus: HealthStatus.Degraded,
                tags: new[] { "infrastructure", "memory" });

        var serviceProvider = services.BuildServiceProvider();

        var dbConfig = serviceProvider.GetService<DatabaseConfiguration>();
        if (!string.IsNullOrEmpty(dbConfig?.ConnectionString))
        {
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            var dbLogger = loggerFactory.CreateLogger<DictionaryImporter.AI.Infrastructure.HealthChecks.DatabaseHealthCheck>();

            services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    "ai_database",
                    sp => new DictionaryImporter.AI.Infrastructure.HealthChecks.DatabaseHealthCheck(
                        dbConfig.ConnectionString, dbLogger),
                    failureStatus: HealthStatus.Degraded,
                    tags: new[] { "database", "infrastructure" }));
        }

        var loggerFactory2 = serviceProvider.GetService<ILoggerFactory>();
        if (loggerFactory2 != null)
        {
            var urlLogger = loggerFactory2.CreateLogger<DictionaryImporter.AI.Infrastructure.HealthChecks.UrlHealthCheck>();

            services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    "openrouter_api",
                    sp => new DictionaryImporter.AI.Infrastructure.HealthChecks.UrlHealthCheck(
                        new Uri("https://api.openrouter.ai/api/v1/models"),
                        TimeSpan.FromSeconds(10),
                        urlLogger),
                    failureStatus: HealthStatus.Degraded,
                    tags: new[] { "api", "external", "openrouter" }));

            services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    "gemini_api",
                    sp => new DictionaryImporter.AI.Infrastructure.HealthChecks.UrlHealthCheck(
                        new Uri("https://generativelanguage.googleapis.com/v1beta/models"),
                        TimeSpan.FromSeconds(10),
                        urlLogger),
                    failureStatus: HealthStatus.Degraded,
                    tags: new[] { "api", "external", "gemini" }));
        }
    }

    public static class HealthCheckBuilderExtensions
    {
        public static IHealthChecksBuilder AddMemoryHealthCheck(
            IHealthChecksBuilder builder,
            string name,
            long maximumMemoryBytes,
            HealthStatus? failureStatus = null,
            IEnumerable<string> tags = null)
        {
            return builder.Add(new HealthCheckRegistration(
                name,
                sp => new MemoryHealthCheck(maximumMemoryBytes),
                failureStatus,
                tags));
        }
    }
}

public class NullQuotaManager : IQuotaManager
{
    public Task<QuotaCheckResult> CheckQuotaAsync(string providerName, string userId = null,
        int estimatedTokens = 0, decimal estimatedCost = 0)
    {
        return Task.FromResult(new QuotaCheckResult { CanProceed = true });
    }

    public Task<QuotaUsageResult> RecordUsageAsync(string providerName, string userId = null,
        int tokensUsed = 0, decimal costUsed = 0, bool success = true)
    {
        return Task.FromResult(new QuotaUsageResult());
    }

    public Task<IEnumerable<QuotaStatus>> GetProviderQuotasAsync(string providerName) =>
        Task.FromResult(Enumerable.Empty<QuotaStatus>());

    public Task<IEnumerable<QuotaStatus>> GetUserQuotasAsync(string userId) =>
        Task.FromResult(Enumerable.Empty<QuotaStatus>());

    public Task ResetExpiredQuotasAsync() => Task.CompletedTask;
}

public class NullAuditLogger : IAuditLogger
{
    public Task LogRequestAsync(AuditLogEntry entry) => Task.CompletedTask;

    public Task<IEnumerable<AuditLogEntry>> GetRecentRequestsAsync(
        string providerName = null, string userId = null, int limit = 100) =>
        Task.FromResult(Enumerable.Empty<AuditLogEntry>());

    public Task<IEnumerable<AuditSummary>> GetAuditSummaryAsync(DateTime from, DateTime to) =>
        Task.FromResult(Enumerable.Empty<AuditSummary>());
}

public class NullResponseCache : IResponseCache
{
    public Task<CachedResponse> GetCachedResponseAsync(string cacheKey) =>
        Task.FromResult<CachedResponse>(null);

    public Task SetCachedResponseAsync(string cacheKey, CachedResponse response, TimeSpan ttl) =>
        Task.CompletedTask;

    public Task RemoveCachedResponseAsync(string cacheKey) => Task.CompletedTask;

    public Task CleanExpiredCacheAsync() => Task.CompletedTask;
}

public class NullMetricsCollector : IPerformanceMetricsCollector
{
    public Task RecordMetricsAsync(ProviderMetrics metrics) => Task.CompletedTask;

    public Task<ProviderPerformance> GetProviderPerformanceAsync(
        string providerName, DateTime from, DateTime to) =>
        Task.FromResult(new ProviderPerformance());

    public Task<IEnumerable<ProviderPerformance>> GetAllProvidersPerformanceAsync(
        DateTime from, DateTime to) =>
        Task.FromResult(Enumerable.Empty<ProviderPerformance>());
}