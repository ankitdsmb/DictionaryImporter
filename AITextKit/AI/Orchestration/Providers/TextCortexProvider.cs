namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers
{
    [Provider("TextCortex", Priority = 5, SupportsCaching = true)]
    public class TextCortexProvider : ChatCompletionProviderBase
    {
        private const string DefaultModel = "gpt-4";
        private const string DefaultBaseUrl = "https://api.textcortex.com/v1/texts/completions";

        public override string ProviderName => "TextCortex";
        public override int Priority => 5;

        public TextCortexProvider(
            HttpClient httpClient,
            ILogger<TextCortexProvider> logger,
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
                Logger.LogWarning("TextCortex API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ChatCompletion = true;
            Capabilities.MaxTokensLimit = 4000;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "de", "fr", "es", "it", "nl", "pl", "pt", "ro", "ru" });
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
                frequency_penalty = 0.0,
                presence_penalty = 0.0,
                n = 1,
                stream = false
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("outputs", out var outputs))
                {
                    var firstOutput = outputs.EnumerateArray().FirstOrDefault();
                    if (firstOutput.TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? string.Empty;
                    }
                }

                if (data.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }
            }

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

            throw new FormatException("Could not find completion text in TextCortex response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("total_tokens", out var totalTokens))
                    return totalTokens.GetInt64();

                if (usage.TryGetProperty("prompt_tokens", out var promptTokens) &&
                    usage.TryGetProperty("completion_tokens", out var completionTokens))
                {
                    return promptTokens.GetInt64() + completionTokens.GetInt64();
                }
            }

            return base.EstimateTokenUsageFromResponse(rootElement);
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("gpt-4"))
            {
                var inputCostPerToken = 0.00003m;
                var outputCostPerToken = 0.00006m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("gpt-3.5-turbo"))
            {
                var inputCostPerToken = 0.0000015m;
                var outputCostPerToken = 0.000002m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("claude-3"))
            {
                var inputCostPerToken = 0.000003m;
                var outputCostPerToken = 0.000015m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["textcortex"] = true;
            response.Metadata["multilingual"] = true;
            response.Metadata["european_provider"] = true;
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
                       message.Contains("limit") ||
                       message.Contains("rate limit") ||
                       message.Contains("insufficient credits");
            }

            return base.ShouldFallback(exception);
        }
    }
}