using System.Text.Json;
using System.Text.Json.Serialization;
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
    [Provider("NLPCloud", Priority = 16, SupportsCaching = true)]
    public class NlpCloudProvider : TextCompletionProviderBase
    {
        private const string DefaultModel = "finetuned-llama-2-70b";
        private const string DefaultBaseUrl = "https://api.nlpcloud.io/v1/gpu/finetuned-llama-2-70b/generation";

        public override string ProviderName => "NLPCloud";
        public override int Priority => 16;

        public NlpCloudProvider(
            HttpClient httpClient,
            ILogger<NlpCloudProvider> logger,
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
                Logger.LogWarning("NLP Cloud API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 1024;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "fr", "de", "es", "it", "nl", "pt" });
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Token {GetApiKey()}");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                text = request.Prompt,
                model = Configuration.Model ?? DefaultModel,
                max_length = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                top_p = 0.9,
                top_k = 50,
                repetition_penalty = 1.0,
                num_return_sequences = 1,
                bad_words = Array.Empty<string>(),
                remove_input = false
            };
        }

        protected override JsonSerializerOptions CreateJsonSerializerOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("generated_text", out var generatedText))
            {
                return generatedText.GetString() ?? string.Empty;
            }

            throw new FormatException("Could not find generated_text in NLP Cloud response");
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("finetuned-llama-2-70b"))
            {
                var costPerToken = 0.0000025m;
                return (inputTokens + outputTokens) * costPerToken;
            }
            else if (model.Contains("gpt-j") || model.Contains("neo"))
            {
                var costPerToken = 0.0000005m;
                return (inputTokens + outputTokens) * costPerToken;
            }

            return (inputTokens + outputTokens) * 0.000001m;
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["nlp_cloud"] = true;
            response.Metadata["gpu_accelerated"] = true;
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
                       message.Contains("free tier");
            }

            return base.ShouldFallback(exception);
        }
    }
}