using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers
{
    [Provider("HuggingFace", Priority = 2, SupportsCaching = true)]
    public class HuggingFaceProvider : TextCompletionProviderBase
    {
        private const string DefaultModel = "gpt2";
        private const string DefaultBaseUrl = "https://api-inference.huggingface.co/models/{model}";

        public HuggingFaceProvider(HttpClient httpClient, ILogger logger, IOptions<ProviderConfiguration> configuration, IQuotaManager quotaManager = null, IAuditLogger auditLogger = null, IResponseCache responseCache = null, IPerformanceMetricsCollector metricsCollector = null, IApiKeyManager apiKeyManager = null) : base(httpClient, logger, configuration, quotaManager, auditLogger, responseCache, metricsCollector, apiKeyManager)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("ElevenLabs API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        public override string ProviderName => "HuggingFace";
        public override int Priority => 2;

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 250;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "ru", "zh", "ja", "ko" });
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetApiKey()}");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                inputs = request.Prompt,
                parameters = new
                {
                    max_new_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                    temperature = Math.Clamp(request.Temperature, 0.1, 2.0),
                    top_p = 0.95,
                    top_k = 50,
                    repetition_penalty = 1.0,
                    do_sample = true,
                    return_full_text = false,
                    num_return_sequences = 1
                }
            };
        }

        protected override HttpRequestMessage CreateHttpRequest(object payload)
        {
            var model = Configuration.Model ?? DefaultModel;
            var url = (Configuration.BaseUrl ?? DefaultBaseUrl).Replace("{model}", model);
            return base.CreateHttpRequest(payload);
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.ValueKind == JsonValueKind.Array)
            {
                var firstElement = rootElement.EnumerateArray().FirstOrDefault();
                if (firstElement.TryGetProperty("generated_text", out var generatedText))
                {
                    return generatedText.GetString() ?? string.Empty;
                }
            }
            else if (rootElement.TryGetProperty("generated_text", out var generatedText))
            {
                return generatedText.GetString() ?? string.Empty;
            }

            throw new FormatException("Invalid Hugging Face response format");
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("gpt2") || model.Contains("distilgpt2"))
                return 0m;
            else if (model.Contains("bloom") || model.Contains("t5"))
                return (inputTokens + outputTokens) * 0.0000001m;
            else if (model.Contains("llama") || model.Contains("falcon"))
                return (inputTokens + outputTokens) * 0.000001m;

            return 0m;
        }
    }
}