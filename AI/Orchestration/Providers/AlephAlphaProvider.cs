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
    [Provider("AlephAlpha", Priority = 11, SupportsCaching = true)]
    public class AlephAlphaProvider : TextCompletionProviderBase
    {
        private const string DefaultModel = "luminous-base";
        private const string DefaultBaseUrl = "https://api.aleph-alpha.com/complete";

        public override string ProviderName => "AlephAlpha";
        public override int Priority => 11;

        public AlephAlphaProvider(
            HttpClient httpClient,
            ILogger<AlephAlphaProvider> logger,
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
            Capabilities.MaxTokensLimit = 2048;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "de", "fr", "es", "it", "nl" });
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
                model = Configuration.Model ?? DefaultModel,
                prompt = request.Prompt,
                maximum_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                top_k = 0,
                top_p = 0.9,
                presence_penalty = 0.0,
                frequency_penalty = 0.0,
                repetition_penalties_include_prompt = false,
                best_of = 1,
                stop_sequences = Array.Empty<string>()
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
            if (rootElement.TryGetProperty("completions", out var completions))
            {
                var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                if (firstCompletion.TryGetProperty("completion", out var completion))
                {
                    return completion.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find completions in Aleph Alpha response");
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("luminous-extended"))
            {
                var inputCostPerToken = 0.000005m;
                var outputCostPerToken = 0.00001m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("luminous-supreme"))
            {
                var inputCostPerToken = 0.00001m;
                var outputCostPerToken = 0.00002m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("luminous-base"))
            {
                var inputCostPerToken = 0.0000025m;
                var outputCostPerToken = 0.000005m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var response = base.CreateSuccessResponse(content, tokensUsed, elapsedTime);
            response.Metadata["aleph_alpha"] = true;
            response.Metadata["european_data_center"] = true;
            response.Metadata["german_ai"] = true;
            return response;
        }
    }
}