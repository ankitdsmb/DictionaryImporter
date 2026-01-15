using System.Text.Json;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Providers.Base;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("Ollama", Priority = 99, SupportsCaching = true)]
    public class OllamaProvider : TextCompletionProviderBase
    {
        private const string DefaultModel = "llama2";
        private const string DefaultBaseUrl = "http://localhost:11434/api/generate";

        public override string ProviderName => "Ollama";
        public override int Priority => 99;
        public override bool IsLocal => true;

        public OllamaProvider(
            HttpClient httpClient,
            ILogger<OllamaProvider> logger,
            IOptions<ProviderConfiguration> configuration,
            IQuotaManager quotaManager = null,
            IAuditLogger auditLogger = null,
            IResponseCache responseCache = null,
            IPerformanceMetricsCollector metricsCollector = null,
            IApiKeyManager apiKeyManager = null)
            : base(httpClient, logger, configuration, quotaManager, auditLogger,
                  responseCache, metricsCollector, apiKeyManager)
        {
            if (string.IsNullOrEmpty(Configuration.BaseUrl))
            {
                Logger.LogInformation("Ollama using default local URL: {BaseUrl}", DefaultBaseUrl);
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 2000;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                model = Configuration.Model ?? DefaultModel,
                prompt = request.Prompt,
                stream = false,
                options = new
                {
                    temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                    num_predict = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit)
                }
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("response", out var response))
            {
                return response.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            return 0m;
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["ollama"] = true;
            response.Metadata["local"] = true;
            response.Metadata["offline_capable"] = true;
            response.Metadata["self_hosted"] = true;
            response.Metadata["no_api_key_needed"] = true;
            response.EstimatedCost = 0m;
            return response;
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("connection refused") ||
                       message.Contains("cannot connect") ||
                       message.Contains("no route to host");
            }

            return false;
        }
    }
}