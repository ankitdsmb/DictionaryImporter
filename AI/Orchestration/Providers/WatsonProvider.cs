using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Providers.Base;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("Watson", Priority = 13, SupportsCaching = true)]
    public class WatsonProvider : ChatCompletionProviderBase
    {
        private const string DefaultModel = "ibm/granite-13b-chat-v2";
        private const string DefaultBaseUrl = "https://us-south.ml.cloud.ibm.com/ml/v1/text/generation";

        public override string ProviderName => "Watson";
        public override int Priority => 13;

        public WatsonProvider(
            HttpClient httpClient,
            ILogger<WatsonProvider> logger,
            IOptions<ProviderConfiguration> configuration,
            IQuotaManager quotaManager = null,
            IAuditLogger auditLogger = null,
            IResponseCache responseCache = null,
            IPerformanceMetricsCollector metricsCollector = null,
            IApiKeyManager apiKeyManager = null)
            : base(httpClient, logger, configuration, quotaManager, auditLogger,
                  responseCache, metricsCollector, apiKeyManager)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("Watson API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 4096;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "pt", "nl" });
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetApiKey()}");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override HttpRequestMessage CreateHttpRequest(object payload)
        {
            var url = Configuration.BaseUrl ?? DefaultBaseUrl;
            url = $"{url}?version=2023-05-29";
            return base.CreateHttpRequest(payload);
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                input = request.Prompt,
                parameters = new
                {
                    max_new_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                    temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                    top_p = 0.9,
                    top_k = 50,
                    repetition_penalty = 1.0
                },
                model_id = Configuration.Model ?? DefaultModel
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("results", out var results))
            {
                var firstResult = results.EnumerateArray().FirstOrDefault();
                if (firstResult.TryGetProperty("generated_text", out var generatedText))
                {
                    return generatedText.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find results in Watson response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("results", out var results))
            {
                var firstResult = results.EnumerateArray().FirstOrDefault();
                if (firstResult.TryGetProperty("generated_token_count", out var tokenCount))
                {
                    return tokenCount.GetInt64();
                }
            }

            return base.EstimateTokenUsageFromResponse(rootElement);
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("granite-13b"))
            {
                var costPerToken = 0.0000025m;
                return (inputTokens + outputTokens) * costPerToken;
            }
            else if (model.Contains("granite-8b"))
            {
                var costPerToken = 0.0000015m;
                return (inputTokens + outputTokens) * costPerToken;
            }

            return (inputTokens + outputTokens) * 0.000002m;
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["watson"] = true;
            response.Metadata["ibm_cloud"] = true;
            return response;
        }
    }
}