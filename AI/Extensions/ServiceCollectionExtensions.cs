using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Infrastructure.Implementations;
using DictionaryImporter.AI.Orchestration;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DictionaryImporter.AI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddRefactoredAiOrchestration(
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

            services.TryAddSingleton<IProviderFactory, CompleteProviderFactory>();
            services.TryAddSingleton<ICompletionOrchestrator, IntelligentOrchestrator>();

            RegisterHttpClients(services, aiConfig);

            RegisterAllProviders(services);

            RegisterInfrastructure(services, aiConfig);

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
                foreach (var providerSection in providersSection.GetChildren())
                {
                    var providerConfig = new ProviderConfiguration();
                    providerSection.Bind(providerConfig);
                    providerConfig.Name = providerSection.Key;

                    if (providerConfig.Capabilities == null)
                    {
                        providerConfig.Capabilities = new ProviderCapabilitiesConfiguration
                        {
                            TextCompletion = true,
                            SupportedLanguages = new List<string> { "en" },
                            MaxTokensLimit = 4000
                        };
                    }

                    aiConfig.Providers[providerSection.Key] = providerConfig;
                }
            }

            if (aiConfig.FallbackOrder == null || !aiConfig.FallbackOrder.Any())
            {
                aiConfig.FallbackOrder = new List<string>
                {
                    "OpenRouter",
                    "Gemini",
                    "Anthropic",
                    "TogetherAI",
                    "Cohere",
                    "AI21",
                    "TextCortex",
                    "Perplexity",
                    "NLPCloud",
                    "HuggingFace",
                    "DeepAI",
                    "Watson",
                    "AlephAlpha",
                    "AlephAlphaVision",
                    "Replicate",
                    "Ollama",
                    "StabilityAI",
                    "ElevenLabs",
                    "AssemblyAI"
                };
            }

            configure?.Invoke(aiConfig);
            return aiConfig;
        }

        private static void RegisterHttpClients(
            IServiceCollection services,
            AiOrchestrationConfiguration aiConfig)
        {
            foreach (var providerConfig in aiConfig.Providers.Values.Where(p => p.IsEnabled))
            {
                services.AddHttpClient(providerConfig.Name, client =>
                {
                    var timeoutSeconds = providerConfig.TimeoutSeconds > 0
                        ? providerConfig.TimeoutSeconds + 5
                        : 35;
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");

                    if (!string.IsNullOrEmpty(providerConfig.BaseUrl))
                    {
                        try
                        {
                            client.BaseAddress = new Uri(providerConfig.BaseUrl);
                        }
                        catch (UriFormatException ex)
                        {
                        }
                    }
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                           System.Net.DecompressionMethods.Deflate,
                    UseCookies = false,
                    AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                })
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));

                switch (providerConfig.Name)
                {
                    case "OpenRouter":
                        services.AddHttpClient<OpenRouterProvider>(client =>
                        {
                            ConfigureOpenRouterClient(client, providerConfig);
                        })
                        .ConfigurePrimaryHttpMessageHandler(CreateDefaultHandler)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                        break;

                    case "Anthropic":
                        services.AddHttpClient<AnthropicProvider>(client =>
                        {
                            ConfigureAnthropicClient(client, providerConfig);
                        })
                        .ConfigurePrimaryHttpMessageHandler(CreateDefaultHandler)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                        break;

                    case "Gemini":
                        services.AddHttpClient<GeminiProvider>(client =>
                        {
                            ConfigureGeminiClient(client, providerConfig);
                        })
                        .ConfigurePrimaryHttpMessageHandler(CreateDefaultHandler)
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                        break;
                }
            }
        }

        private static void ConfigureOpenRouterClient(HttpClient client, ProviderConfiguration config)
        {
            var timeoutSeconds = config.TimeoutSeconds > 0 ? config.TimeoutSeconds + 5 : 35;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(config.BaseUrl))
            {
                try
                {
                    client.BaseAddress = new Uri(config.BaseUrl);
                }
                catch (UriFormatException) { }
            }
        }

        private static void ConfigureAnthropicClient(HttpClient client, ProviderConfiguration config)
        {
            var timeoutSeconds = config.TimeoutSeconds > 0 ? config.TimeoutSeconds + 5 : 35;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            if (!string.IsNullOrEmpty(config.BaseUrl))
            {
                try
                {
                    client.BaseAddress = new Uri(config.BaseUrl);
                }
                catch (UriFormatException) { }
            }
        }

        private static void ConfigureGeminiClient(HttpClient client, ProviderConfiguration config)
        {
            var timeoutSeconds = config.TimeoutSeconds > 0 ? config.TimeoutSeconds + 5 : 35;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(config.BaseUrl))
            {
                try
                {
                    client.BaseAddress = new Uri(config.BaseUrl);
                }
                catch (UriFormatException) { }
            }
        }

        private static HttpMessageHandler CreateDefaultHandler()
        {
            return new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                       System.Net.DecompressionMethods.Deflate,
                UseCookies = false,
                AllowAutoRedirect = false,
                MaxConnectionsPerServer = 50,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    errors == System.Net.Security.SslPolicyErrors.None
            };
        }

        private static void RegisterAllProviders(IServiceCollection services)
        {
            services.TryAddTransient<Ai21Provider>();
            services.TryAddTransient<CohereProvider>();
            services.TryAddTransient<DeepAiProvider>();
            services.TryAddTransient<HuggingFaceProvider>();
            services.TryAddTransient<NlpCloudProvider>();
            services.TryAddTransient<OllamaProvider>();
            services.TryAddTransient<ReplicateProvider>();

            services.TryAddTransient<OpenRouterProvider>();
            services.TryAddTransient<AnthropicProvider>();
            services.TryAddTransient<GeminiProvider>();
            services.TryAddTransient<PerplexityProvider>();
            services.TryAddTransient<TextCortexProvider>();
            services.TryAddTransient<TogetherAiProvider>();
            services.TryAddTransient<WatsonProvider>();

            services.TryAddTransient<AlephAlphaProvider>();
            services.TryAddTransient<AlephAlphaVisionProvider>();

            services.TryAddTransient<AssemblyAiProvider>();
            services.TryAddTransient<ElevenLabsProvider>();

            services.TryAddTransient<StabilityAiProvider>();
        }

        private static void RegisterInfrastructure(
            IServiceCollection services,
            AiOrchestrationConfiguration aiConfig)
        {
            if (aiConfig.EnableQuotaManagement)
            {
                services.TryAddSingleton<IQuotaManager, SqlQuotaManager>();
            }
            else
            {
                services.TryAddSingleton<IQuotaManager, NullQuotaManager>();
            }

            if (aiConfig.EnableAuditLogging)
            {
                services.TryAddSingleton<IAuditLogger, SqlAuditLogger>();
            }
            else
            {
                services.TryAddSingleton<IAuditLogger, NullAuditLogger>();
            }

            if (aiConfig.EnableCaching)
            {
                services.TryAddSingleton<IResponseCache, DistributedResponseCache>();
            }
            else
            {
                services.TryAddSingleton<IResponseCache, NullResponseCache>();
            }

            if (aiConfig.EnableMetricsCollection)
            {
                services.TryAddSingleton<IPerformanceMetricsCollector, SqlMetricsCollector>();
            }
            else
            {
                services.TryAddSingleton<IPerformanceMetricsCollector, NullMetricsCollector>();
            }

            services.TryAddSingleton<ConfigurationValidator>();

            services.TryAddSingleton<IApiKeyManager, ConfigurationApiKeyManager>();
        }

        public static IServiceCollection ReplaceAiOrchestration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var serviceDescriptors = services.Where(s =>
                s.ServiceType == typeof(IProviderFactory) ||
                s.ServiceType == typeof(ICompletionOrchestrator) ||
                s.ServiceType.Name.Contains("Provider") ||
                s.ServiceType.Name.Contains("Orchestrator")).ToList();

            foreach (var descriptor in serviceDescriptors)
            {
                services.Remove(descriptor);
            }

            return services.AddRefactoredAiOrchestration(configuration);
        }
    }
}