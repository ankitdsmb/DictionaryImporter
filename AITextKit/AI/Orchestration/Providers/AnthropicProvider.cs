namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers
{
    [Provider("Anthropic", Priority = 4, SupportsCaching = true)]
    public class AnthropicProvider : ChatCompletionProviderBase
    {
        private const string DefaultModel = "claude-3-haiku-20240307";
        private const string DefaultBaseUrl = "https://api.anthropic.com/v1/messages";

        public override string ProviderName => "Anthropic";
        public override int Priority => 4;
        public override bool SupportsVision => true;

        public AnthropicProvider(
            HttpClient httpClient,
            ILogger<AnthropicProvider> logger,
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
                Logger.LogWarning("Anthropic API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ImageAnalysis = true;
            Capabilities.MaxTokensLimit = 4096;
        }

        protected override void ConfigureAuthentication()
        {
            var apiKey = GetApiKey();
            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            HttpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            HttpClient.DefaultRequestHeaders.Add("anthropic-beta", "max-tokens-2024-07-15");
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateRequestPayload(AiRequest request)
        {
            if (request.ImageData != null || request.ImageUrls?.Count > 0)
                return CreateVisionPayload(request);

            return base.CreateRequestPayload(request);
        }

        private object CreateVisionPayload(AiRequest request)
        {
            var model = Configuration.Model ?? DefaultModel;
            var content = new List<object>
            {
                new { type = "text", text = request.Prompt }
            };

            if (request.ImageData != null)
            {
                var base64Image = Convert.ToBase64String(request.ImageData);
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = GetMimeType(request.ImageFormat),
                        data = base64Image
                    }
                });
            }
            else if (request.ImageUrls?.Count > 0)
            {
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "url",
                        url = request.ImageUrls.First(),
                        media_type = "image/jpeg"
                    }
                });
            }

            return new
            {
                model,
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                messages = new[]
                {
                    new { role = "user", content }
                },
                system = request.SystemPrompt ?? "You are a helpful AI assistant."
            };
        }

        private string GetMimeType(string imageFormat)
        {
            return imageFormat?.ToLower() switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;

            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var inputTokens))
                    tokenUsage += inputTokens.GetInt64();
                if (usage.TryGetProperty("output_tokens", out var outputTokens))
                    tokenUsage += outputTokens.GetInt64();
            }

            return ExtractCompletionText(root);
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("content", out var contentArray))
            {
                var firstContent = contentArray.EnumerateArray().FirstOrDefault();
                if (firstContent.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find content in Anthropic response");
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("claude-3-opus"))
            {
                var inputCostPerToken = 0.000015m;
                var outputCostPerToken = 0.000075m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("claude-3-sonnet"))
            {
                var inputCostPerToken = 0.000003m;
                var outputCostPerToken = 0.000015m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("claude-3-haiku"))
            {
                var inputCostPerToken = 0.00000025m;
                var outputCostPerToken = 0.00000125m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }
    }
}