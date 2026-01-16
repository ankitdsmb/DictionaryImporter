using ILogger = Microsoft.Extensions.Logging.ILogger;
using JsonException = System.Text.Json.JsonException;

namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers.Base
{
    public abstract class VisionProviderBase(
        HttpClient httpClient,
        ILogger logger,
        IOptions<ProviderConfiguration> configuration,
        IQuotaManager quotaManager = null,
        IAuditLogger auditLogger = null,
        IResponseCache responseCache = null,
        IPerformanceMetricsCollector metricsCollector = null,
        IApiKeyManager apiKeyManager = null)
        : AiProviderBase(httpClient, logger, configuration, quotaManager, auditLogger,
            responseCache, metricsCollector, apiKeyManager)
    {
        public override ProviderType Type => ProviderType.VisionAnalysis;
        public override bool SupportsVision => true;

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ImageAnalysis = true;
            Capabilities.MaxTokensLimit = 2048;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ValidateRequest(AiRequest request)
        {
            base.ValidateRequest(request);

            if (!HasImageInput(request))
            {
                throw new ArgumentException(
                    $"Vision provider {ProviderName} requires image input. " +
                    "Provide ImageData, ImageUrls, or image parameters.");
            }
        }

        protected virtual bool HasImageInput(AiRequest request)
        {
            return request.ImageData != null ||
                   request.ImageUrls?.Count > 0 ||
                   request.AdditionalParameters?.ContainsKey("image_url") == true ||
                   request.AdditionalParameters?.ContainsKey("image_base64") == true ||
                   request.Prompt?.Contains("data:image/") == true && request.Prompt.Contains("base64");
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return CreateVisionPayload(request);
        }

        protected abstract object CreateVisionPayload(AiRequest request);

        protected virtual object CreateMultimodalPayload(AiRequest request, string model)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["prompt"] = request.Prompt ?? "Describe this image",
                ["max_tokens"] = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                ["temperature"] = Math.Clamp(request.Temperature, 0.0, 1.0)
            };

            if (request.ImageData != null)
            {
                AddImageDataToPayload(payload, request);
            }
            else if (request.ImageUrls?.Count > 0)
            {
                AddImageUrlsToPayload(payload, request);
            }
            else if (request.AdditionalParameters != null)
            {
                AddImageParametersToPayload(payload, request);
            }

            return payload;
        }

        protected virtual void AddImageDataToPayload(Dictionary<string, object> payload, AiRequest request)
        {
            var base64Image = Convert.ToBase64String(request.ImageData);
            payload["image"] = new
            {
                type = "base64",
                data = base64Image,
                mime_type = AiProviderHelper.GetMimeType(request.ImageFormat)
            };
        }

        protected virtual void AddImageUrlsToPayload(Dictionary<string, object> payload, AiRequest request)
        {
            var imageUrl = request.ImageUrls.First();
            payload["image_url"] = imageUrl;
        }

        protected virtual void AddImageParametersToPayload(Dictionary<string, object> payload, AiRequest request)
        {
            if (request.AdditionalParameters.TryGetValue("image_url", out var imageUrl))
            {
                payload["image_url"] = imageUrl;
            }
            else if (request.AdditionalParameters.TryGetValue("image_base64", out var imageBase64))
            {
                payload["image"] = new
                {
                    type = "base64",
                    data = imageBase64,
                    mime_type = "image/jpeg"
                };
            }
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;

            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);
                var root = jsonDoc.RootElement;

                if (AiProviderHelper.HasError(root, out var errorMessage))
                {
                    if (AiProviderHelper.IsQuotaError(errorMessage))
                        throw new ProviderQuotaExceededException(ProviderName, $"{ProviderName} error: {errorMessage}");

                    throw new HttpRequestException($"{ProviderName} API error: {errorMessage}");
                }

                var result = ExtractVisionResponse(root);
                tokenUsage = EstimateVisionTokenUsage(result);

                return result;
            }
            catch (JsonException ex)
            {
                Logger.LogError(ex, "Failed to parse {Provider} JSON response", ProviderName);
                throw new FormatException($"Invalid {ProviderName} response format");
            }
        }

        protected abstract string ExtractVisionResponse(JsonElement rootElement);

        protected virtual long EstimateVisionTokenUsage(string response)
        {
            var baseTokens = response.Length / 4;
            return baseTokens + 1000;
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var inputCostPerToken = 0.000005m;
            var outputCostPerToken = 0.00001m;
            return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
        }
    }
}