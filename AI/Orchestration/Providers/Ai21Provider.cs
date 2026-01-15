using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Helpers;
using DictionaryImporter.AI.Orchestration.Providers.Base;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("AI21", Priority = 7, SupportsCaching = true)]
    public class Ai21Provider : TextCompletionProviderBase
    {
        private const string DefaultModel = "j2-light";
        private const string DefaultBaseUrl = "https://api.ai21.com/studio/v1/{model}/complete";

        public override string ProviderName => "AI21";
        public override int Priority => 7;

        public Ai21Provider(
            HttpClient httpClient,
            ILogger<Ai21Provider> logger,
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
                Logger.LogWarning("AI21 API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 512;
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
                prompt = request.Prompt,
                numResults = 1,
                maxTokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                topKReturn = 0,
                topP = 0.95,
                stopSequences = Array.Empty<string>(),
                countPenalty = new
                {
                    scale = 0,
                    applyToNumbers = false,
                    applyToPunctuation = false,
                    applyToStopwords = false,
                    applyToWhitespaces = false,
                    applyToEmojis = false
                },
                frequencyPenalty = new
                {
                    scale = 0,
                    applyToNumbers = false,
                    applyToPunctuation = false,
                    applyToStopwords = false,
                    applyToWhitespaces = false,
                    applyToEmojis = false
                },
                presencePenalty = new
                {
                    scale = 0,
                    applyToNumbers = false,
                    applyToPunctuation = false,
                    applyToStopwords = false,
                    applyToWhitespaces = false,
                    applyToEmojis = false
                }
            };
        }

        protected override HttpRequestMessage CreateHttpRequest(object payload)
        {
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
            var url = (Configuration.BaseUrl ?? DefaultBaseUrl).Replace("{model}", model);
            return AiProviderHelper.CreateJsonRequest(payload, url);
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("completions", out var completions))
            {
                var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                if (firstCompletion.TryGetProperty("data", out var data))
                {
                    return data.GetProperty("text").GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find completions in AI21 response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            long tokenUsage = 0;

            if (rootElement.TryGetProperty("prompt", out var prompt) &&
                prompt.TryGetProperty("tokens", out var promptTokens))
            {
                tokenUsage += promptTokens.EnumerateArray().Count();
            }

            return tokenUsage;
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("j2-ultra"))
            {
                var costPerToken = 0.0000185m;
                return (inputTokens + outputTokens) * costPerToken;
            }
            else if (model.Contains("j2-mid"))
            {
                var costPerToken = 0.00001m;
                return (inputTokens + outputTokens) * costPerToken;
            }
            else if (model.Contains("j2-light"))
            {
                var costPerToken = 0.000003m;
                return (inputTokens + outputTokens) * costPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["free_tier_max_tokens"] = 512;
            response.Metadata["ai21"] = true;
            return response;
        }
    }
}