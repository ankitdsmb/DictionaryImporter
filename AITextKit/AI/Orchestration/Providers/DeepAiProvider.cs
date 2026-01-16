namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers
{
    [Provider("DeepAI", Priority = 4, SupportsCaching = true)]
    public class DeepAiProvider : TextCompletionProviderBase
    {
        private const string DefaultModel = "text-davinci-003-free";
        private const string DefaultBaseUrl = "https://api.deepai.org/api/text-generator";

        public override string ProviderName => "DeepAI";
        public override int Priority => 4;

        public DeepAiProvider(
            HttpClient httpClient,
            ILogger<DeepAiProvider> logger,
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
                Logger.LogWarning("DeepAI API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 300;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("api-key", GetApiKey());
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override void ValidateRequest(AiRequest request)
        {
            base.ValidateRequest(request);

            if (request.Prompt.Length > 4000)
                throw new ArgumentException($"DeepAI prompt exceeds 4000 character limit. Length: {request.Prompt.Length}");
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                text = request.Prompt,
                model = Configuration.Model ?? DefaultModel,
                temperature = Math.Clamp(request.Temperature, 0.1, 1.0),
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit)
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("output", out var outputElement))
            {
                return outputElement.GetString() ?? string.Empty;
            }

            if (rootElement.TryGetProperty("text", out var textElement))
            {
                return textElement.GetString() ?? string.Empty;
            }

            if (rootElement.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.TryGetProperty("output", out var nestedOutput))
                {
                    return nestedOutput.GetString() ?? string.Empty;
                }
            }

            foreach (var property in rootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Name != "id" &&
                    property.Name != "model")
                {
                    return property.Value.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            var responseText = ExtractCompletionText(rootElement);
            return EstimateTokenUsage(responseText);
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("text-davinci-003-free"))
            {
                return 0m;
            }
            else if (model.Contains("text-davinci"))
            {
                var costPerToken = 0.000001m;
                return (inputTokens + outputTokens) * costPerToken;
            }

            return (inputTokens + outputTokens) * 0.0000005m;
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["deepai"] = true;
            response.Metadata["free_tier_max_tokens"] = 300;
            return response;
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is ProviderQuotaExceededException || exception is RateLimitExceededException)
                return true;

            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("429") ||
                       message.Contains("401") ||
                       message.Contains("403") ||
                       message.Contains("503") ||
                       message.Contains("quota") ||
                       message.Contains("limit");
            }

            return base.ShouldFallback(exception);
        }
    }
}