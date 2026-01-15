using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Providers.Base;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("Replicate", Priority = 12, SupportsCaching = true)]
    public class ReplicateProvider : TextCompletionProviderBase
    {
        private const string DefaultModel = "meta/llama-2-70b-chat";
        private const string DefaultBaseUrl = "https://api.replicate.com/v1/predictions";

        public override string ProviderName => "Replicate";
        public override int Priority => 12;

        public ReplicateProvider(
            HttpClient httpClient,
            ILogger<ReplicateProvider> logger,
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
                Logger.LogWarning("Replicate API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.MaxTokensLimit = 500;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Token {GetApiKey()}");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ValidateRequest(request);
                EnsureProviderEnabled();
                await CheckAndEnforceQuotaAsync(request);

                var cached = await TryGetCachedResponseAsync(request);
                if (cached != null) return cached;

                var predictionId = await CreatePredictionAsync(request, cancellationToken);

                var result = await PollPredictionAsync(predictionId, cancellationToken);
                stopwatch.Stop();

                var tokenUsage = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var response = CreateSuccessResponse(result, tokenUsage, stopwatch.Elapsed);
                response.Metadata["replicate"] = true;
                response.Metadata["open_source"] = true;
                response.Metadata["prediction_id"] = predictionId;

                await RecordUsageAsync(request, response, stopwatch.Elapsed, request.Context?.UserId);

                if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
                {
                    await CacheResponseAsync(request, response,
                        TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "Replicate provider failed");

                if (ShouldFallback(ex)) throw;
                return CreateErrorResponse(ex, stopwatch.Elapsed, request);
            }
        }

        private async Task<string> CreatePredictionAsync(
            AiRequest request, CancellationToken cancellationToken)
        {
            var modelVersion = GetModelVersion();
            var payload = new
            {
                version = modelVersion,
                input = new
                {
                    prompt = request.Prompt,
                    max_length = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                    temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                    top_p = 0.9,
                    repetition_penalty = 1.0
                }
            };

            var httpRequest = CreateHttpRequest(payload);
            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken), cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDoc = JsonDocument.Parse(content);

            return jsonDoc.RootElement.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("No prediction ID received");
        }

        private async Task<string> PollPredictionAsync(
            string predictionId, CancellationToken cancellationToken)
        {
            var pollUrl = $"{Configuration.BaseUrl ?? DefaultBaseUrl}/{predictionId}";

            for (int i = 0; i < 60; i++)
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, pollUrl);
                var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                using var jsonDoc = JsonDocument.Parse(content);
                var status = jsonDoc.RootElement.GetProperty("status").GetString();

                if (status == "succeeded")
                {
                    var output = jsonDoc.RootElement.GetProperty("output");
                    return string.Join(" ", output.EnumerateArray().Select(x => x.GetString()));
                }
                else if (status == "failed" || status == "canceled")
                {
                    var error = jsonDoc.RootElement.GetProperty("error").GetString() ?? "Unknown error";
                    throw new HttpRequestException($"Replicate prediction failed: {error}");
                }

                await Task.Delay(5000, cancellationToken);
            }

            throw new TimeoutException("Replicate prediction timeout after 5 minutes");
        }

        private string GetModelVersion()
        {
            var modelVersions = new Dictionary<string, string>
            {
                ["meta/llama-2-70b-chat"] = "02e509c789964a7ea8736978a43525956ef40397be9033abf9fd2badfe68c9e3",
                ["mistralai/mistral-7b-instruct-v0.1"] = "5fe0a3d7ac2852264a25279d1dfb798acbc4d49711d126646594e212cb821749",
                ["google/flan-t5-xxl"] = "b7a93e2e1c9542794c5c0b6d7a78ef59d672b5c5b0c4c5c5f5a5c5d5e5f5a5b5c"
            };

            var model = Configuration.Model ?? DefaultModel;
            return modelVersions.GetValueOrDefault(model, "02e509c789964a7ea8736978a43525956ef40397be9033abf9fd2badfe68c9e3");
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new { };
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;
            return string.Empty;
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            throw new NotImplementedException();
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("llama-2-70b"))
            {
                var seconds = (inputTokens + outputTokens) / 100;
                var costPerSecond = 0.0183m;
                return seconds * costPerSecond;
            }
            else if (model.Contains("mistral-7b"))
            {
                var seconds = (inputTokens + outputTokens) / 200;
                var costPerSecond = 0.0033m;
                return seconds * costPerSecond;
            }

            var defaultSeconds = (inputTokens + outputTokens) / 150;
            var defaultCostPerSecond = 0.0083m;
            return defaultSeconds * defaultCostPerSecond;
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
                       message.Contains("credit");
            }

            return base.ShouldFallback(exception);
        }
    }
}