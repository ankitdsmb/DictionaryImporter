using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Orchestration;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.Extensions.Http;

namespace DictionaryImporter.AI.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAiOrchestration(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<OrchestrationConfiguration> configure = null)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var orchestrationConfig = new OrchestrationConfiguration();
            configuration.GetSection("AI:Orchestration").Bind(orchestrationConfig);
            configure?.Invoke(orchestrationConfig);
            services.AddSingleton(orchestrationConfig);

            RegisterHttpClients(services);

            RegisterProviders(services);

            services.AddSingleton<IProviderFactory, ProviderConfigurationManager>();

            services.AddSingleton<ICompletionOrchestrator, IntelligentOrchestrator>();

            return services;
        }

        private static void RegisterHttpClients(IServiceCollection services)
        {
            var providerNames = new[]
            {
                "OpenRouter", "Anthropic", "Gemini", "TogetherAI", "Cohere",
                "AI21", "TextCortex", "Perplexity", "NLPCloud", "HuggingFace",
                "DeepAI", "Watson", "AlephAlpha", "AlephAlphaVision", "Replicate",
                "Ollama", "StabilityAI", "ElevenLabs", "AssemblyAI"
            };

            foreach (var providerName in providerNames)
            {
                services.AddHttpClient(providerName, (client) =>
                {
                    client.Timeout = TimeSpan.FromSeconds(35);

                    client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                    UseProxy = true,
                    Proxy = null,
                    UseCookies = false
                })
                .AddPolicyHandler((serviceProvider, request) =>
                {
                    var logger = serviceProvider.GetRequiredService<ILogger<HttpClient>>();

                    return HttpPolicyExtensions
                        .HandleTransientHttpError()
                        .OrResult(msg => (int)msg.StatusCode >= 500)
                        .WaitAndRetryAsync(
                            3,
                            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            onRetry: (outcome, timespan, retryCount, context) =>
                            {
                                logger.LogWarning(
                                    "Retry {RetryCount}/3 for {Provider}. Waiting {Delay}ms. Status: {Status}",
                                    retryCount,
                                    providerName,
                                    timespan.TotalMilliseconds,
                                    outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message);
                            });
                })
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(
                    TimeSpan.FromSeconds(40)));
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
    }
}