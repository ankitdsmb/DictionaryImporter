using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Orchestration.Providers;

namespace DictionaryImporter.AI.Infrastructure
{
    public class CompleteProviderFactory : IProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AiOrchestrationConfiguration _config;
        private readonly ILogger<CompleteProviderFactory> _logger;

        private readonly Dictionary<string, Type> _providerTypes;

        public CompleteProviderFactory(
            IServiceProvider serviceProvider,
            IOptions<AiOrchestrationConfiguration> config,
            ILogger<CompleteProviderFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _config = config.Value;
            _logger = logger;

            _providerTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                ["AI21"] = typeof(Ai21Provider),
                ["Cohere"] = typeof(CohereProvider),
                ["DeepAI"] = typeof(DeepAiProvider),
                ["HuggingFace"] = typeof(HuggingFaceProvider),
                ["NLPCloud"] = typeof(NlpCloudProvider),
                ["Ollama"] = typeof(OllamaProvider),
                ["Replicate"] = typeof(ReplicateProvider),
                ["OpenRouter"] = typeof(OpenRouterProvider),
                ["Anthropic"] = typeof(AnthropicProvider),
                ["Gemini"] = typeof(GeminiProvider),
                ["Perplexity"] = typeof(PerplexityProvider),
                ["TextCortex"] = typeof(TextCortexProvider),
                ["TogetherAI"] = typeof(TogetherAiProvider),
                ["Watson"] = typeof(WatsonProvider),
                ["AlephAlpha"] = typeof(AlephAlphaProvider),
                ["AlephAlphaVision"] = typeof(AlephAlphaVisionProvider),
                ["AssemblyAI"] = typeof(AssemblyAiProvider),
                ["ElevenLabs"] = typeof(ElevenLabsProvider),
                ["StabilityAI"] = typeof(StabilityAiProvider)
            };

            _logger.LogInformation("Provider factory initialized with {Count} providers", _providerTypes.Count);
        }

        public ICompletionProvider CreateProvider(string providerName)
        {
            if (string.IsNullOrEmpty(providerName))
                throw new ArgumentException("Provider name cannot be null or empty", nameof(providerName));

            if (!_providerTypes.TryGetValue(providerName, out var providerType))
                throw new ArgumentException($"Unknown provider: {providerName}", nameof(providerName));

            if (!_config.Providers.TryGetValue(providerName, out var providerConfig))
            {
                providerConfig = new ProviderConfiguration
                {
                    Name = providerName,
                    IsEnabled = false
                };
                _logger.LogWarning("Using default configuration for provider: {Provider}", providerName);
            }

            if (!providerConfig.IsEnabled)
                throw new InvalidOperationException($"Provider {providerName} is disabled");

            try
            {
                var provider = ActivatorUtilities.CreateInstance(_serviceProvider, providerType) as ICompletionProvider
                    ?? throw new InvalidOperationException($"Failed to create instance of {providerType.Name}");

                _logger.LogDebug("Created provider: {Provider}", providerName);
                return provider;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create provider: {Provider}", providerName);
                throw new InvalidOperationException($"Failed to create provider {providerName}", ex);
            }
        }

        public IEnumerable<ICompletionProvider> GetAllProviders()
        {
            var providers = new List<ICompletionProvider>();

            foreach (var providerName in _providerTypes.Keys)
            {
                try
                {
                    var provider = CreateProvider(providerName);
                    if (provider != null && provider.IsEnabled)
                    {
                        providers.Add(provider);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping provider: {Provider}", providerName);
                }
            }

            return providers.OrderBy(p => p.Priority);
        }

        public IEnumerable<ICompletionProvider> GetProvidersForType(RequestType requestType)
        {
            return GetAllProviders()
                .Where(p => p.CanHandleRequest(new AiRequest { Type = requestType }))
                .OrderBy(p => p.Priority);
        }

        public async Task<bool> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
        {
            var validProviders = new List<string>();
            var invalidProviders = new List<string>();

            foreach (var providerName in _providerTypes.Keys)
            {
                if (!_config.Providers.TryGetValue(providerName, out var config) || !config.IsEnabled)
                {
                    _logger.LogDebug("Provider {Provider} is disabled", providerName);
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
                            MaxTokens = 5,
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
                            validProviders.Add(providerName);
                            _logger.LogDebug("Provider {Provider} configuration valid", providerName);
                        }
                        else
                        {
                            invalidProviders.Add(providerName);
                            _logger.LogWarning("Provider {Provider} configuration invalid: {Error}",
                                providerName, response.ErrorMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    invalidProviders.Add(providerName);
                    _logger.LogWarning(ex, "Provider {Provider} configuration invalid", providerName);
                }
            }

            _logger.LogInformation(
                "Configuration validation completed: {Valid}/{Total} providers valid",
                validProviders.Count, _providerTypes.Count);

            if (invalidProviders.Any())
            {
                _logger.LogWarning("Invalid providers: {Providers}", string.Join(", ", invalidProviders));
            }

            return validProviders.Count > 0;
        }
    }
}