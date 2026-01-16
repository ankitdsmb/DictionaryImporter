namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers
{
    [Provider("Cohere", Priority = 6, SupportsCaching = true)]
    public class CohereProvider : TextCompletionProviderBase
    {
        private const string DefaultModel = "command-light";
        private const string DefaultBaseUrl = "https://api.cohere.ai/v1/generate";

        public override string ProviderName => "Cohere";
        public override int Priority => 6;

        public CohereProvider(
            HttpClient httpClient,
            ILogger<CohereProvider> logger,
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
                Logger.LogWarning("Cohere API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 4000;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            AiProviderHelper.SetBearerAuth(HttpClient, GetApiKey());
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                model = Configuration.Model ?? DefaultModel,
                prompt = request.Prompt,
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                k = 0,
                p = 0.75,
                frequency_penalty = 0.0,
                presence_penalty = 0.0,
                stop_sequences = Array.Empty<string>(),
                return_likelihoods = "NONE"
            };
        }

        protected override HttpRequestMessage CreateHttpRequest(object payload)
        {
            var url = Configuration.BaseUrl ?? DefaultBaseUrl;
            return AiProviderHelper.CreateJsonRequestWithSnakeCase(payload, url);
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("generations", out var generations))
            {
                var firstGeneration = generations.EnumerateArray().FirstOrDefault();
                if (firstGeneration.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find generations in Cohere response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("tokens", out var tokens))
            {
                long tokenUsage = 0;

                if (tokens.TryGetProperty("input_tokens", out var inputTokens))
                    tokenUsage += inputTokens.GetInt64();

                if (tokens.TryGetProperty("output_tokens", out var outputTokens))
                    tokenUsage += outputTokens.GetInt64();

                return tokenUsage;
            }

            return 0;
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("command-r") || model.Contains("command-r-plus"))
            {
                var inputCostPerToken = 0.0000005m;
                var outputCostPerToken = 0.0000015m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("command"))
            {
                var inputCostPerToken = 0.0000015m;
                var outputCostPerToken = 0.000005m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("embed"))
            {
                var costPerToken = 0.0000001m;
                return (inputTokens + outputTokens) * costPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }
    }
}