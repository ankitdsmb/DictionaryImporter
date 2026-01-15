using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Infrastructure.Implementations;

public class EnhancedProviderFactory(
    IServiceProvider serviceProvider,
    IOptions<AiOrchestrationConfiguration> config,
    ILogger<EnhancedProviderFactory> logger)
    : IProviderFactory
{
    private readonly AiOrchestrationConfiguration _config = config.Value;

    private readonly Dictionary<string, Type> _providerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OpenRouter"] = typeof(OpenRouterProvider),
        ["Anthropic"] = typeof(AnthropicProvider),
        ["Gemini"] = typeof(GeminiProvider),
        ["TogetherAI"] = typeof(TogetherAiProvider),
        ["Cohere"] = typeof(CohereProvider),
        ["AI21"] = typeof(Ai21Provider),
        ["TextCortex"] = typeof(TextCortexProvider),
        ["Perplexity"] = typeof(PerplexityProvider),
        ["NLPCloud"] = typeof(NlpCloudProvider),
        ["HuggingFace"] = typeof(HuggingFaceProvider),
        ["DeepAI"] = typeof(DeepAiProvider),
        ["Watson"] = typeof(WatsonProvider),
        ["AlephAlpha"] = typeof(AlephAlphaProvider),
        ["AlephAlphaVision"] = typeof(AlephAlphaVisionProvider),
        ["Replicate"] = typeof(ReplicateProvider),
        ["Ollama"] = typeof(OllamaProvider),
        ["StabilityAI"] = typeof(StabilityAiProvider),
        ["ElevenLabs"] = typeof(ElevenLabsProvider),
        ["AssemblyAI"] = typeof(AssemblyAiProvider)
    };

    public ICompletionProvider CreateProvider(string providerName)
    {
        if (!_providerTypes.TryGetValue(providerName, out var providerType))
        {
            throw new ArgumentException($"Unknown provider: {providerName}");
        }

        if (!_config.Providers.TryGetValue(providerName, out var providerConfig))
        {
            providerConfig = new ProviderConfiguration
            {
                Name = providerName,
                IsEnabled = false
            };
        }

        if (!providerConfig.IsEnabled)
        {
            throw new InvalidOperationException($"Provider {providerName} is disabled");
        }

        try
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(providerName);

            var loggerType = typeof(ILogger<>).MakeGenericType(providerType);
            var logger = serviceProvider.GetService(loggerType);

            var options = Options.Create(providerConfig);

            var quotaManager = serviceProvider.GetService<IQuotaManager>();
            var auditLogger = serviceProvider.GetService<IAuditLogger>();
            var responseCache = serviceProvider.GetService<IResponseCache>();
            var metricsCollector = serviceProvider.GetService<IPerformanceMetricsCollector>();
            var apiKeyManager = serviceProvider.GetService<IApiKeyManager>();

            var instance = Activator.CreateInstance(
                providerType,
                httpClient,
                logger,
                options,
                quotaManager,
                auditLogger,
                responseCache,
                metricsCollector,
                apiKeyManager);

            return (ICompletionProvider)instance;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create provider instance for {Provider}", providerName);
            throw new InvalidOperationException($"Failed to create provider {providerName}", ex);
        }
    }

    public IEnumerable<ICompletionProvider> GetProvidersForType(RequestType requestType)
    {
        var providers = new List<ICompletionProvider>();

        foreach (var providerName in _providerTypes.Keys)
        {
            try
            {
                var provider = CreateProvider(providerName);
                if (provider != null && provider.CanHandleRequest(new AiRequest { Type = requestType }))
                {
                    providers.Add(provider);
                }
            }
            catch
            {
                continue;
            }
        }

        return providers.OrderBy(p => p.Priority);
    }

    public IEnumerable<ICompletionProvider> GetAllProviders()
    {
        var providers = new List<ICompletionProvider>();

        foreach (var providerName in _providerTypes.Keys)
        {
            try
            {
                var provider = CreateProvider(providerName);
                if (provider != null)
                {
                    providers.Add(provider);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to create provider {Provider}", providerName);
            }
        }

        return providers;
    }

    public async Task<bool> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var validConfigurations = new List<string>();
        var invalidConfigurations = new List<string>();

        foreach (var kvp in _config.Providers)
        {
            var providerName = kvp.Key;
            var config = kvp.Value;

            if (!config.IsEnabled)
            {
                logger.LogDebug("Provider {Provider} is disabled", providerName);
                continue;
            }

            try
            {
                var provider = CreateProvider(providerName);
                if (provider != null)
                {
                    var testRequest = new AiRequest
                    {
                        Prompt = "Test",
                        MaxTokens = 1,
                        Type = RequestType.TextCompletion,
                        Context = new RequestContext
                        {
                            RequestId = $"test-{Guid.NewGuid()}",
                            UserId = "system"
                        }
                    };

                    var response = await provider.GetCompletionAsync(testRequest, cancellationToken);

                    if (response.IsSuccess)
                    {
                        validConfigurations.Add(providerName);
                        logger.LogInformation(
                            "Configuration valid for provider {Provider}",
                            providerName);
                    }
                    else
                    {
                        invalidConfigurations.Add(providerName);
                        logger.LogWarning(
                            "Configuration invalid for provider {Provider}: {Error}",
                            providerName, response.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                invalidConfigurations.Add(providerName);
                logger.LogError(
                    ex, "Configuration invalid for provider {Provider}",
                    providerName);
            }
        }

        logger.LogInformation(
            "Configuration validation completed: {Valid}/{Total} valid configurations",
            validConfigurations.Count, _config.Providers.Count);

        return validConfigurations.Count > 0;
    }
}