using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Helpers;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("Cohere", Priority = 6, SupportsCaching = true)]
    public class CohereProvider : EnhancedBaseProvider
    {
        private const string DefaultModel = "command-light";
        private const string BaseUrl = "https://api.cohere.ai/v1/generate";
        private AiRequest _currentRequest;

        public override string ProviderName => "Cohere";
        public override int Priority => 6;
        public override ProviderType Type => ProviderType.TextCompletion;
        public override bool SupportsAudio => false;
        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public CohereProvider(
            HttpClient httpClient,
            ILogger<CohereProvider> logger,
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
                Logger.LogWarning("Cohere API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.MaxTokensLimit = 4000;
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
                _currentRequest = request;

                // Validate request
                AiProviderHelper.ValidateCommonRequest(request, Capabilities, Logger);

                // Check if provider is enabled
                if (!Configuration.IsEnabled)
                    throw new InvalidOperationException("Cohere provider is disabled");

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

                Logger.LogDebug("Sending request to Cohere with model {Model}", model);

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
                        ["cohere"] = true
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
                Logger.LogError(ex, "Cohere provider failed for request {RequestId}", request.Context?.RequestId);

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
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                k = 0,
                p = 0.75,
                frequency_penalty = 0.0,
                presence_penalty = 0.0,
                stop_sequences = Array.Empty<string>(),
                return_likelihoods = "NONE"
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
                if (root.TryGetProperty("message", out var messageElement))
                {
                    var errorMessage = messageElement.GetString() ?? "Unknown error";
                    if (AiProviderHelper.IsQuotaError(errorMessage))
                        throw new ProviderQuotaExceededException(ProviderName, $"Cohere error: {errorMessage}");
                    throw new HttpRequestException($"Cohere API error: {errorMessage}");
                }

                // Extract token usage
                if (root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("tokens", out var tokens))
                {
                    if (tokens.TryGetProperty("input_tokens", out var inputTokens))
                        tokenUsage += inputTokens.GetInt64();
                    if (tokens.TryGetProperty("output_tokens", out var outputTokens))
                        tokenUsage += outputTokens.GetInt64();
                }
                else
                {
                    // Estimate token usage
                    if (_currentRequest != null)
                    {
                        tokenUsage = AiProviderHelper.EstimateTokenUsage(_currentRequest.Prompt);
                    }
                }

                // Extract generation text
                if (root.TryGetProperty("generations", out var generations))
                {
                    var firstGeneration = generations.EnumerateArray().FirstOrDefault();
                    if (firstGeneration.TryGetProperty("text", out var textElement))
                    {
                        var resultText = textElement.GetString() ?? string.Empty;

                        // Update token usage if not extracted
                        if (tokenUsage == 0 && _currentRequest != null)
                        {
                            tokenUsage = AiProviderHelper.EstimateTokenUsage(_currentRequest.Prompt) +
                                        AiProviderHelper.EstimateTokenUsage(resultText);
                        }

                        return resultText;
                    }
                }
                throw new FormatException("Could not find generations in Cohere response");
            }
            catch (JsonException ex)
            {
                Logger.LogError(ex, "Failed to parse Cohere JSON response");
                throw new FormatException("Invalid Cohere response format");
            }
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("command-r") || model.Contains("command-r-plus"))
            {
                var inputCostPerToken = 0.0000005m;
                var outputCostPerToken = 0.0000015m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("command"))
            {
                var inputCostPerToken = 0.0000015m;
                var outputCostPerToken = 0.000005m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("embed"))
            {
                var costPerToken = 0.0000001m;
                return (inputTokens + outputTokens) * costPerToken;
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