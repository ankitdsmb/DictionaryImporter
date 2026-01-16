namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers
{
    [Provider("TogetherAI", Priority = 10, SupportsCaching = true)]
    public class TogetherAiProvider : ChatCompletionProviderBase
    {
        private const string DefaultModel = "mistralai/Mixtral-8x7B-Instruct-v0.1";
        private const string DefaultBaseUrl = "https://api.together.xyz/v1/chat/completions";

        public override string ProviderName => "TogetherAI";
        public override int Priority => 10;

        public TogetherAiProvider(
            HttpClient httpClient,
            ILogger<TogetherAiProvider> logger,
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
                Logger.LogWarning("TogetherAI API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ChatCompletion = true;
            Capabilities.MaxTokensLimit = 8192;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "nl", "pt", "ru", "zh", "ja", "ko" });
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetApiKey()}");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateRequestPayload(AiRequest request)
        {
            var messages = new List<object>();

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                messages.Add(new { role = "system", content = request.SystemPrompt });
            }

            messages.Add(new { role = "user", content = request.Prompt });

            return new
            {
                model = Configuration.Model ?? DefaultModel,
                messages,
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 2.0),
                top_p = 0.9,
                top_k = 50,
                repetition_penalty = 1.0,
                stop = Array.Empty<string>(),
                stream = false
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("choices", out var choices))
            {
                var firstChoice = choices.EnumerateArray().FirstOrDefault();
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                if (firstChoice.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find choices in TogetherAI response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("usage", out var usage))
            {
                long totalTokens = 0;

                if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
                    totalTokens += promptTokens.GetInt64();

                if (usage.TryGetProperty("completion_tokens", out var completionTokens))
                    totalTokens += completionTokens.GetInt64();

                if (usage.TryGetProperty("total_tokens", out var total))
                    return total.GetInt64();

                return totalTokens;
            }

            return base.EstimateTokenUsageFromResponse(rootElement);
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("mixtral-8x7b") || model.Contains("Mixtral-8x7B"))
            {
                var costPerMillionTokens = 0.60m;
                var totalTokens = inputTokens + outputTokens;
                return totalTokens / 1_000_000m * costPerMillionTokens;
            }
            else if (model.Contains("llama-2-70b") || model.Contains("Llama-2-70b"))
            {
                var costPerMillionTokens = 0.90m;
                var totalTokens = inputTokens + outputTokens;
                return totalTokens / 1_000_000m * costPerMillionTokens;
            }
            else if (model.Contains("codellama") || model.Contains("CodeLlama"))
            {
                var costPerMillionTokens = 0.90m;
                var totalTokens = inputTokens + outputTokens;
                return totalTokens / 1_000_000m * costPerMillionTokens;
            }
            else if (model.Contains("qwen") || model.Contains("Qwen"))
            {
                var costPerMillionTokens = 0.20m;
                var totalTokens = inputTokens + outputTokens;
                return totalTokens / 1_000_000m * costPerMillionTokens;
            }

            var defaultCostPerMillionTokens = 0.50m;
            var defaultTotalTokens = inputTokens + outputTokens;
            return defaultTotalTokens / 1_000_000m * defaultCostPerMillionTokens;
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["togetherai"] = true;
            response.Metadata["open_source"] = true;
            response.Metadata["large_context"] = true;
            response.Metadata["model_type"] = GetModelType(Configuration.Model ?? DefaultModel);
            return response;
        }

        private string GetModelType(string model)
        {
            if (model.Contains("mixtral")) return "mixtral";
            if (model.Contains("llama")) return "llama";
            if (model.Contains("qwen")) return "qwen";
            if (model.Contains("codellama")) return "codellama";
            if (model.Contains("mistral")) return "mistral";
            return "unknown";
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
                       message.Contains("limit") ||
                       message.Contains("rate limit") ||
                       message.Contains("insufficient credits") ||
                       message.Contains("out of capacity");
            }

            return base.ShouldFallback(exception);
        }
    }
}