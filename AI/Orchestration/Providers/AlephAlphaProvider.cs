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
    [Provider("AlephAlpha", Priority = 11, SupportsCaching = true)]
    public class AlephAlphaProvider : EnhancedBaseProvider
    {
        private const string DefaultModel = "luminous-base";
        private const string BaseUrl = "https://api.aleph-alpha.com/complete";
        private AiRequest _currentRequest;

        public override string ProviderName => "AlephAlpha";
        public override int Priority => 11;
        public override ProviderType Type => ProviderType.TextCompletion;
        public override bool SupportsAudio => false;
        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public AlephAlphaProvider(
            HttpClient httpClient,
            ILogger<AlephAlphaProvider> logger,
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
                Logger.LogWarning("Aleph Alpha API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.MaxTokensLimit = 2048;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "de", "fr", "es", "it", "nl" });
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
                _currentRequest = request;

                // Validate request
                AiProviderHelper.ValidateCommonRequest(request, Capabilities, Logger);

                // Check if provider is enabled
                if (!Configuration.IsEnabled)
                    throw new InvalidOperationException("Aleph Alpha provider is disabled");

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

                // Create payload
                var payload = CreateRequestPayload(request);
                var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
                var url = Configuration.BaseUrl ?? BaseUrl;

                Logger.LogDebug("Sending request to Aleph Alpha with model {Model}", model);

                // Create and send request
                var httpRequest = AiProviderHelper.CreateJsonRequestWithSnakeCase(payload, url);
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
                        ["aleph_alpha"] = true,
                        ["european_data_center"] = true,
                        ["german_ai"] = true
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
                Logger.LogError(ex, "Aleph Alpha provider failed for request {RequestId}", request.Context?.RequestId);

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
            finally
            {
                _currentRequest = null;
            }
        }

        private object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
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
                        throw new ProviderQuotaExceededException(ProviderName, $"Aleph Alpha error: {errorMessage}");
                    throw new HttpRequestException($"Aleph Alpha API error: {errorMessage}");
                }

                // Extract completion text
                if (root.TryGetProperty("completions", out var completions))
                {
                    var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                    if (firstCompletion.TryGetProperty("completion", out var completion))
                    {
                        var resultText = completion.GetString() ?? string.Empty;

                        // Estimate token usage
                        if (_currentRequest != null)
                        {
                            tokenUsage = AiProviderHelper.EstimateTokenUsage(_currentRequest.Prompt) +
                                        AiProviderHelper.EstimateTokenUsage(resultText);
                        }
                        else
                        {
                            tokenUsage = AiProviderHelper.EstimateTokenUsage(resultText);
                        }

                        return resultText;
                    }
                }
                throw new FormatException("Could not find completions in Aleph Alpha response");
            }
            catch (JsonException ex)
            {
                Logger.LogError(ex, "Failed to parse Aleph Alpha JSON response");
                throw new FormatException("Invalid Aleph Alpha response format");
            }
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("luminous-extended"))
            {
                var inputCostPerToken = 0.000005m;
                var outputCostPerToken = 0.00001m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("luminous-supreme"))
            {
                var inputCostPerToken = 0.00001m;
                var outputCostPerToken = 0.00002m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("luminous-base"))
            {
                var inputCostPerToken = 0.0000025m;
                var outputCostPerToken = 0.000005m;
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