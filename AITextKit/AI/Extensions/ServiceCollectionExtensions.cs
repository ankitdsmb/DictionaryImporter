using DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;
using DictionaryImporter.AITextKit.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DictionaryImporter.AITextKit.AI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAiOrchestration(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<AiOrchestrationConfiguration> configure = null)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var aiConfig = LoadConfiguration(configuration, configure);

            // Keep singleton config instance (used by CompleteProviderFactory + infra)
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

            // ✅ Bind Orchestration section values into aiConfig
            configuration.GetSection("AI:Orchestration").Bind(aiConfig);

            // ✅ Ensure Providers dictionary exists
            aiConfig.Providers ??= new Dictionary<string, ProviderConfiguration>(StringComparer.OrdinalIgnoreCase);

            // ✅ Bind Providers properly from "AI:Providers"
            var providersSection = configuration.GetSection("AI:Providers");
            if (providersSection.Exists())
            {
                foreach (var providerSection in providersSection.GetChildren())
                {
                    var providerConfig = new ProviderConfiguration();
                    providerSection.Bind(providerConfig);

                    providerConfig.Name = providerSection.Key;

                    providerConfig.Capabilities ??= new ProviderCapabilitiesConfiguration
                    {
                        TextCompletion = true,
                        SupportedLanguages = new List<string> { "en" },
                        MaxTokensLimit = 4000
                    };

                    aiConfig.Providers[providerSection.Key] = providerConfig;
                }
            }

            // ✅ FallbackOrder default
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
                // Named HttpClient (generic)
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
                        // NOTE: Your BaseUrl values are full endpoints in JSON.
                        // Providers should handle absolute URIs properly.
                        try
                        {
                            client.BaseAddress = new Uri(providerConfig.BaseUrl);
                        }
                        catch (UriFormatException)
                        {
                            // ignore invalid uri
                        }
                    }
                })
                .ConfigurePrimaryHttpMessageHandler(CreateDefaultHandler)
                .SetHandlerLifetime(TimeSpan.FromMinutes(5));

                // Typed HttpClients for specific providers
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

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(config.BaseUrl))
            {
                try
                {
                    client.BaseAddress = new Uri(config.BaseUrl);
                }
                catch (UriFormatException)
                {
                }
            }
        }

        private static void ConfigureAnthropicClient(HttpClient client, ProviderConfiguration config)
        {
            var timeoutSeconds = config.TimeoutSeconds > 0 ? config.TimeoutSeconds + 5 : 35;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            if (!string.IsNullOrEmpty(config.BaseUrl))
            {
                try
                {
                    client.BaseAddress = new Uri(config.BaseUrl);
                }
                catch (UriFormatException)
                {
                }
            }
        }

        private static void ConfigureGeminiClient(HttpClient client, ProviderConfiguration config)
        {
            var timeoutSeconds = config.TimeoutSeconds > 0 ? config.TimeoutSeconds + 5 : 35;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(config.BaseUrl))
            {
                try
                {
                    client.BaseAddress = new Uri(config.BaseUrl);
                }
                catch (UriFormatException)
                {
                }
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

                // ✅ SAFE SSL validation (do not use "=> true")
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

            return services.AddAiOrchestration(configuration);
        }
    }
}