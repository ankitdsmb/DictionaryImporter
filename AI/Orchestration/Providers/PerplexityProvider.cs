using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Providers.Base;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("Perplexity", Priority = 9, SupportsCaching = true)]
    public class PerplexityProvider : ChatCompletionProviderBase
    {
        private const string DefaultModel = "sonar-small-online";
        private const string DefaultBaseUrl = "https://api.perplexity.ai/chat/completions";

        public override string ProviderName => "Perplexity";
        public override int Priority => 9;

        public PerplexityProvider(
            HttpClient httpClient,
            ILogger<PerplexityProvider> logger,
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
                Logger.LogWarning("Perplexity API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ChatCompletion = true;
            Capabilities.MaxTokensLimit = 4000;
            Capabilities.SupportedLanguages.Add("en");
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
                stream = false,
                search_domain_filter = Array.Empty<string>(),
                return_images = false,
                return_related_questions = false
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("choices", out var choices))
            {
                var firstChoice = choices.EnumerateArray().FirstOrDefault();
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    return message.GetProperty("content").GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find choices in Perplexity response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("usage", out var usage))
            {
                return usage.GetProperty("total_tokens").GetInt64();
            }

            return base.EstimateTokenUsageFromResponse(rootElement);
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("sonar-pro"))
            {
                var costPerToken = 0.000005m;
                return (inputTokens + outputTokens) * costPerToken;
            }
            else if (model.Contains("sonar"))
            {
                var costPerToken = 0.000001m;
                return (inputTokens + outputTokens) * costPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["perplexity"] = true;
            response.Metadata["web_search"] = (Configuration.Model ?? DefaultModel).Contains("online");
            response.Metadata["real_time_data"] = (Configuration.Model ?? DefaultModel).Contains("online");
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
                       message.Contains("free tier");
            }

            return base.ShouldFallback(exception);
        }
    }
}