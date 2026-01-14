using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Orchestration.Providers;
using System.ComponentModel.DataAnnotations;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace DictionaryImporter.AI.Configuration
{
    public class ProviderConfigurationManager : IProviderFactory
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProviderConfigurationManager> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, ProviderConfiguration> _configurations;
        private readonly Dictionary<string, Type> _providerTypes;

        public ProviderConfigurationManager(
            IConfiguration configuration,
            ILogger<ProviderConfigurationManager> logger,
            IServiceProvider serviceProvider)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _configurations = new Dictionary<string, ProviderConfiguration>(StringComparer.OrdinalIgnoreCase);
            _providerTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            InitializeProviderTypes();
            LoadConfigurations();
        }

        private void InitializeProviderTypes()
        {
            _providerTypes["OpenRouter"] = typeof(OpenRouterProvider);
        }

        private void LoadConfigurations()
        {
            var providerNames = _providerTypes.Keys;

            foreach (var providerName in providerNames)
            {
                try
                {
                    var config = LoadProviderConfiguration(providerName);
                    ValidateConfiguration(config);
                    _configurations[providerName] = config;

                    _logger.LogDebug(
                        "Loaded configuration for {Provider}: Enabled={IsEnabled}",
                        providerName,
                        config.IsEnabled);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to load configuration for provider {Provider}",
                        providerName);

                    _configurations[providerName] = new ProviderConfiguration
                    {
                        IsEnabled = false
                    };
                }
            }
        }

        private ProviderConfiguration LoadProviderConfiguration(string providerName)
        {
            var section = _configuration.GetSection($"AI:Providers:{providerName}");

            var config = new ProviderConfiguration
            {
                ApiKey = section["ApiKey"] ?? Environment.GetEnvironmentVariable($"AI_{providerName.ToUpper()}_API_KEY") ?? "",
                BaseUrl = section["BaseUrl"] ?? GetDefaultBaseUrl(providerName),
                Model = section["Model"] ?? GetDefaultModel(providerName),
                TimeoutSeconds = section.GetValue("TimeoutSeconds", GetDefaultTimeout(providerName)),
                MaxRetries = section.GetValue("MaxRetries", 2),
                IsEnabled = section.GetValue("IsEnabled", false) && !string.IsNullOrEmpty(section["ApiKey"]),
                CircuitBreakerFailuresBeforeBreaking = section.GetValue("CircuitBreakerFailuresBeforeBreaking", 5),
                CircuitBreakerDurationSeconds = section.GetValue("CircuitBreakerDurationSeconds", 30),
                RequestsPerMinute = section.GetValue("RequestsPerMinute", 60),
                RequestsPerDay = section.GetValue("RequestsPerDay", 1000)
            };

            var freeTierSection = section.GetSection("FreeTier");
            config.FreeTier = new FreeTierLimits
            {
                MaxTokens = freeTierSection.GetValue("MaxTokens", 1000),
                MaxRequestsPerDay = freeTierSection.GetValue("MaxRequestsPerDay", 100),
                MaxImagesPerMonth = freeTierSection.GetValue("MaxImagesPerMonth", 50),
                MaxAudioMinutesPerMonth = freeTierSection.GetValue("MaxAudioMinutesPerMonth", 60),
                MaxCharactersPerMonth = freeTierSection.GetValue("MaxCharactersPerMonth", 10000)
            };

            var additionalSection = section.GetSection("AdditionalSettings");
            foreach (var child in additionalSection.GetChildren())
            {
                config.AdditionalSettings[child.Key] = child.Value;
            }

            return config;
        }

        private void ValidateConfiguration(ProviderConfiguration config)
        {
            var context = new ValidationContext(config);
            var results = new List<ValidationResult>();

            if (!Validator.TryValidateObject(config, context, results, true))
            {
                var errors = string.Join(", ", results.Select(r => r.ErrorMessage));
                throw new ValidationException($"Invalid configuration: {errors}");
            }

            if (config.IsEnabled && string.IsNullOrEmpty(config.ApiKey))
            {
                throw new ValidationException($"API key is required for enabled provider");
            }
        }

        private string GetDefaultBaseUrl(string providerName)
        {
            return providerName switch
            {
                "OpenRouter" => "https://api.openrouter.ai/api/v1/chat/completions",
                "Anthropic" => "https://api.anthropic.com/v1/messages",
                "Gemini" => "https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent",
                "TogetherAI" => "https://api.together.xyz/v1/chat/completions",
                "Cohere" => "https://api.cohere.ai/v1/generate",
                "AI21" => "https://api.ai21.com/studio/v1/j2-light/complete",
                "TextCortex" => "https://api.textcortex.com/v1/texts/completions",
                "Perplexity" => "https://api.perplexity.ai/chat/completions",
                "NLPCloud" => "https://api.nlpcloud.io/v1/gpu/finetuned-llama-2-70b/generation",
                "HuggingFace" => "https://api-inference.huggingface.co/models/gpt2",
                "DeepAI" => "https://api.deepai.org/api/text-generator",
                "Watson" => "https://us-south.ml.cloud.ibm.com/ml/v1/text/generation?version=2023-05-29",
                "AlephAlpha" => "https://api.aleph-alpha.com/complete",
                "AlephAlphaVision" => "https://api.aleph-alpha.com/complete",
                "Replicate" => "https://api.replicate.com/v1/predictions",
                "Ollama" => "http://localhost:11434/api/generate",
                "StabilityAI" => "https://api.stability.ai/v1/generation/stable-diffusion-xl-1024-v1-0/text-to-image",
                "ElevenLabs" => "https://api.elevenlabs.io/v1/text-to-speech",
                "AssemblyAI" => "https://api.assemblyai.com/v2/transcript",
                _ => throw new ArgumentException($"Unknown provider: {providerName}")
            };
        }

        private string GetDefaultModel(string providerName)
        {
            return providerName switch
            {
                "OpenRouter" => "openai/gpt-3.5-turbo",
                "Anthropic" => "claude-3-haiku-20240307",
                "Gemini" => "gemini-pro",
                "TogetherAI" => "mistralai/Mixtral-8x7B-Instruct-v0.1",
                "Cohere" => "command-light",
                "AI21" => "j2-light",
                "TextCortex" => "gpt-4",
                "Perplexity" => "sonar-small-online",
                "NLPCloud" => "finetuned-llama-2-70b",
                "HuggingFace" => "gpt2",
                "DeepAI" => "text-davinci-003-free",
                "Watson" => "ibm/granite-13b-chat-v2",
                "AlephAlpha" => "luminous-base",
                "AlephAlphaVision" => "luminous-base",
                "Replicate" => "meta/llama-2-70b-chat",
                "Ollama" => "llama2",
                "StabilityAI" => "stable-diffusion-xl-1024-v1-0",
                "ElevenLabs" => "21m00Tcm4TlvDq8ikWAM",
                "AssemblyAI" => "enhanced",
                _ => throw new ArgumentException($"Unknown provider: {providerName}")
            };
        }

        private int GetDefaultTimeout(string providerName)
        {
            return providerName switch
            {
                "AssemblyAI" => 120,
                "Replicate" => 180,
                "StabilityAI" => 60,
                "Ollama" => 120,
                _ => 30
            };
        }

        public ICompletionProvider CreateProvider(string providerName)
        {
            if (!_providerTypes.TryGetValue(providerName, out var providerType))
            {
                throw new ArgumentException($"Unknown provider: {providerName}");
            }

            if (!_configurations.TryGetValue(providerName, out var config))
            {
                throw new InvalidOperationException($"Configuration not found for provider: {providerName}");
            }

            if (!config.IsEnabled)
            {
                throw new InvalidOperationException($"Provider {providerName} is disabled");
            }

            try
            {
                var httpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
                HttpClient httpClient;

                if (httpClientFactory != null)
                {
                    httpClient = httpClientFactory.CreateClient(providerName);
                }
                else
                {
                    httpClient = new HttpClient();
                    _logger.LogWarning("IHttpClientFactory not found, creating HttpClient directly for {Provider}", providerName);
                }

                var loggerType = typeof(ILogger<>).MakeGenericType(providerType);
                var logger = _serviceProvider.GetService(loggerType);

                var options = Microsoft.Extensions.Options.Options.Create(config);

                var instance = Activator.CreateInstance(
                    providerType,
                    httpClient,
                    logger,
                    options);

                return (ICompletionProvider)instance;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to create provider instance for {Provider}",
                    providerName);
                throw;
            }
        }

        public IEnumerable<ICompletionProvider> GetProvidersForType(RequestType requestType)
        {
            var providers = new List<ICompletionProvider>();

            var allProviders = GetAllProviders();

            foreach (var provider in allProviders)
            {
                if (provider.CanHandleRequest(new AiRequest { Type = requestType }))
                {
                    providers.Add(provider);
                }
            }

            return providers.OrderBy(p => p.Priority);
        }

        public IEnumerable<ICompletionProvider> GetAllProviders()
        {
            var providers = new List<ICompletionProvider>();

            foreach (var providerName in _configurations.Keys)
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
                    _logger.LogDebug(
                        ex,
                        "Failed to create provider {Provider}",
                        providerName);
                }
            }

            return providers;
        }

        public async Task<bool> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
        {
            var validConfigurations = new List<string>();
            var invalidConfigurations = new List<string>();

            foreach (var kvp in _configurations)
            {
                var providerName = kvp.Key;
                var config = kvp.Value;

                if (!config.IsEnabled)
                {
                    _logger.LogDebug("Provider {Provider} is disabled", providerName);
                    continue;
                }

                try
                {
                    ValidateConfiguration(config);

                    if (!string.IsNullOrEmpty(config.ApiKey))
                    {
                        var provider = CreateProvider(providerName);
                        if (provider != null)
                        {
                            var testRequest = new AiRequest
                            {
                                Prompt = "Test",
                                MaxTokens = 1,
                                Type = RequestType.TextCompletion
                            };

                            var response = await provider.GetCompletionAsync(testRequest, cancellationToken);
                            if (response.IsSuccess)
                            {
                                validConfigurations.Add(providerName);
                                _logger.LogInformation(
                                    "Configuration valid for provider {Provider}",
                                    providerName);
                            }
                            else
                            {
                                invalidConfigurations.Add(providerName);
                                _logger.LogWarning(
                                    "Configuration invalid for provider {Provider}: {Error}",
                                    providerName,
                                    response.ErrorMessage);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    invalidConfigurations.Add(providerName);
                    _logger.LogError(
                        ex,
                        "Configuration invalid for provider {Provider}",
                        providerName);
                }
            }

            _logger.LogInformation(
                "Configuration validation completed: {Valid}/{Total} valid configurations",
                validConfigurations.Count,
                _configurations.Count);

            return validConfigurations.Count > 0;
        }

        public IReadOnlyDictionary<string, ProviderConfiguration> GetAllConfigurations()
        {
            return _configurations;
        }
    }
}