using System.Linq;
using System.Text.Json;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Providers.Base;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("AlephAlphaVision", Priority = 18, SupportsCaching = true)]
    public class AlephAlphaVisionProvider : VisionProviderBase
    {
        private const string DefaultModel = "luminous-base";
        private const string DefaultBaseUrl = "https://api.aleph-alpha.com/complete";

        public override string ProviderName => "AlephAlphaVision";
        public override int Priority => 18;
        public override bool SupportsVision => true;

        public AlephAlphaVisionProvider(
            HttpClient httpClient,
            ILogger<AlephAlphaVisionProvider> logger,
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
                Logger.LogWarning("Aleph Alpha API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.ImageAnalysis = true;
            Capabilities.MaxTokensLimit = 2048;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetApiKey()}");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateVisionPayload(AiRequest request)
        {
            return CreateMultimodalPayload(request, Configuration.Model ?? DefaultModel);
        }

        private object CreateMultimodalPayload(AiRequest request, string model)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["prompt"] = request.Prompt ?? "Describe this image",
                ["maximum_tokens"] = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                ["temperature"] = Math.Clamp(request.Temperature, 0.0, 1.0)
            };

            if (request.AdditionalParameters != null)
            {
                if (request.AdditionalParameters.TryGetValue("image_url", out var imageUrl))
                {
                    payload["image_url"] = imageUrl;
                }
                else if (request.AdditionalParameters.TryGetValue("image_base64", out var imageBase64))
                {
                    payload["image_base64"] = imageBase64;
                }
            }

            return payload;
        }

        protected override string ExtractVisionResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("completions", out var completions))
            {
                var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                if (firstCompletion.TryGetProperty("completion", out var completion))
                {
                    return completion.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find completions in Aleph Alpha Vision response");
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("luminous-extended"))
            {
                var inputCostPerToken = 0.00001m;
                var outputCostPerToken = 0.00002m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("luminous-supreme"))
            {
                var inputCostPerToken = 0.000015m;
                var outputCostPerToken = 0.00003m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }

            var defaultInputCostPerToken = 0.000005m;
            var defaultOutputCostPerToken = 0.00001m;
            return inputTokens * defaultInputCostPerToken + outputTokens * defaultOutputCostPerToken;
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["aleph_alpha"] = true;
            response.Metadata["vision_capabilities"] = true;
            response.Metadata["multimodal"] = true;
            response.Metadata["european_data_center"] = true;
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
                       message.Contains("vision");
            }

            return base.ShouldFallback(exception);
        }
    }
}