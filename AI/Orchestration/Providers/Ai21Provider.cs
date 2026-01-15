using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("AI21", Priority = 7, SupportsCaching = true)]
    public class Ai21Provider : EnhancedBaseProvider
    {
        private const string DefaultModel = "j2-light";
        private const string BaseUrl = "https://api.ai21.com/studio/v1/{model}/complete";

        public override string ProviderName => "AI21";
        public override int Priority => 7;
        public override ProviderType Type => ProviderType.TextCompletion;
        public override bool SupportsAudio => false;
        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public Ai21Provider(
            HttpClient httpClient,
            ILogger<Ai21Provider> logger,
            IOptions<ProviderConfiguration> configuration,
            IQuotaManager quotaManager = null,
            IAuditLogger auditLogger = null,
            IResponseCache responseCache = null,
            IPerformanceMetricsCollector metricsCollector = null,
            IApiKeyManager apiKeyManager = null)
            : base(httpClient, logger, configuration, quotaManager, auditLogger, responseCache, metricsCollector, apiKeyManager)
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
            Capabilities.TextCompletion = true;
            Capabilities.MaxTokensLimit = 512;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            AiProviderHelper.SetBearerAuth(HttpClient, GetApiKey());
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Validate request
                AiProviderHelper.ValidateRequestWithLengthLimit(request, 10000, "AI21");
                AiProviderHelper.ValidateCommonRequest(request, Capabilities, Logger);

                // Check if provider is enabled
                if (!Configuration.IsEnabled)
                    throw new InvalidOperationException("AI21 provider is disabled");

                // Check quota
                var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
                if (!quotaCheck.CanProceed)
                    throw new ProviderQuotaExceededException(ProviderName,
                        $"Quota exceeded. Remaining: {quotaCheck.RemainingRequests} requests, " +
                        $"{quotaCheck.RemainingTokens} tokens. Resets in {quotaCheck.TimeUntilReset.TotalMinutes:F0} minutes.");

                // Check cache
                if (Configuration.EnableCaching)
                {
                    var cachedResponse = await TryGetCachedResponseAsync(request);
                    if (cachedResponse != null) return cachedResponse;
                }

                // Create payload and URL
                var payload = CreateRequestPayload(request);
                var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
                var url = (Configuration.BaseUrl ?? BaseUrl).Replace("{model}", model);

                Logger.LogDebug("Sending request to AI21 with model {Model}", model);

                // Create and send request
                var httpRequest = AiProviderHelper.CreateJsonRequest(payload, url);
                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                // Parse response
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseResponse(content, out var tokenUsage);

                stopwatch.Stop();

                // Create success response
                var aiResponse = AiProviderHelper.CreateSuccessResponse(
                    result,
                    ProviderName,
                    model,
                    tokenUsage,
                    stopwatch.Elapsed,
                    EstimateCost(tokenUsage, 0),
                    new Dictionary<string, object>
                    {
                        ["free_tier_max_tokens"] = 512,
                        ["ai21"] = true
                    });

                // Record usage and cache
                await RecordUsageAsync(request, aiResponse, stopwatch.Elapsed, request.Context?.UserId);

                if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
                {
                    await CacheResponseAsync(request, aiResponse,
                        TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
                }

                return aiResponse;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "AI21 provider failed for request {RequestId}", request.Context?.RequestId);

                if (ShouldFallback(ex)) throw;

                // Create error response
                var errorResponse = AiProviderHelper.CreateErrorResponse(
                    ex,
                    ProviderName,
                    DefaultModel,
                    stopwatch.Elapsed,
                    request,
                    Configuration.Model ?? DefaultModel);

                // Log audit if available
                if (AuditLogger != null)
                {
                    var auditEntry = CreateAuditEntry(request, errorResponse, stopwatch.Elapsed, request.Context?.UserId);
                    auditEntry.ErrorCode = errorResponse.ErrorCode;
                    auditEntry.ErrorMessage = errorResponse.ErrorMessage;
                    await AuditLogger.LogRequestAsync(auditEntry);
                }

                return errorResponse;
            }
        }

        private object CreateRequestPayload(AiRequest request)
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

        private string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;
            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);
                var root = jsonDoc.RootElement;

                // Check for errors
                if (AiProviderHelper.HasError(root, out var errorMessage))
                {
                    if (AiProviderHelper.IsQuotaError(errorMessage))
                        throw new ProviderQuotaExceededException(ProviderName, $"AI21 error: {errorMessage}");
                    throw new HttpRequestException($"AI21 API error: {errorMessage}");
                }

                // Extract completion text
                if (root.TryGetProperty("completions", out var completions))
                {
                    var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                    if (firstCompletion.TryGetProperty("data", out var data))
                    {
                        var text = data.GetProperty("text").GetString() ?? string.Empty;

                        // Estimate token usage
                        if (root.TryGetProperty("prompt", out var prompt) &&
                            prompt.TryGetProperty("tokens", out var promptTokens))
                        {
                            tokenUsage += promptTokens.EnumerateArray().Count();
                        }
                        tokenUsage += text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                        return text;
                    }
                }
                throw new FormatException("Could not find completions in AI21 response");
            }
            catch (JsonException ex)
            {
                Logger.LogError(ex, "Failed to parse AI21 JSON response");
                throw new FormatException("Invalid AI21 response format");
            }
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("j2-ultra"))
            {
                var inputCostPerToken = 0.0000185m;
                var outputCostPerToken = 0.0000185m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("j2-mid"))
            {
                var inputCostPerToken = 0.00001m;
                var outputCostPerToken = 0.00001m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("j2-light"))
            {
                var inputCostPerToken = 0.000003m;
                var outputCostPerToken = 0.000003m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else
            {
                var inputCostPerToken = 0.000001m;
                var outputCostPerToken = 0.000002m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
        }

        public override bool ShouldFallback(Exception exception)
        {
            return AiProviderHelper.ShouldFallbackCommon(exception) || base.ShouldFallback(exception);
        }
    }
}