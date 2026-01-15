using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Helpers;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("DeepAI", Priority = 4, SupportsCaching = true)]
    public class DeepAiProvider : EnhancedBaseProvider
    {
        private const string DefaultModel = "text-davinci-003-free";
        private const string BaseUrl = "https://api.deepai.org/api/text-generator";

        public override string ProviderName => "DeepAI";
        public override int Priority => 4;
        public override ProviderType Type => ProviderType.TextCompletion;
        public override bool SupportsAudio => false;
        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public DeepAiProvider(
            HttpClient httpClient,
            ILogger<DeepAiProvider> logger,
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
                Logger.LogWarning("DeepAI API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.MaxTokensLimit = 300;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            AiProviderHelper.SetApiKeyAuth(HttpClient, GetApiKey());
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Validate request
                AiProviderHelper.ValidateRequestWithLengthLimit(request, 4000, "DeepAI");
                AiProviderHelper.ValidateCommonRequest(request, Capabilities, Logger);

                // Check if provider is enabled
                if (!Configuration.IsEnabled)
                    throw new InvalidOperationException("DeepAI provider is disabled");

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

                Logger.LogDebug("Sending request to DeepAI with model {Model}", model);

                // Create and send request
                var httpRequest = AiProviderHelper.CreateJsonRequest(payload, url);
                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                // Parse response
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseResponse(content);

                stopwatch.Stop();

                // Calculate token usage
                var tokenUsage = CalculateTokenEstimate(request.Prompt, result);

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
                        ["deepai"] = true,
                        ["free_tier_max_tokens"] = 300
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
                Logger.LogError(ex, "DeepAI provider failed for request {RequestId}", request.Context?.RequestId);

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
                text = request.Prompt,
                model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                temperature = Math.Clamp(request.Temperature, 0.1, 1.0),
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit)
            };
        }

        private string ParseResponse(string jsonResponse)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);
                var root = jsonDoc.RootElement;

                // Check for errors
                if (root.TryGetProperty("err", out var errElement))
                {
                    var errorMessage = errElement.GetString() ?? "Unknown error";
                    throw new ProviderQuotaExceededException(ProviderName, $"DeepAI error: {errorMessage}");
                }

                // Try different response formats
                if (root.TryGetProperty("output", out var outputElement))
                {
                    return outputElement.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("data", out var dataElement))
                {
                    if (dataElement.TryGetProperty("output", out var nestedOutput))
                    {
                        return nestedOutput.GetString() ?? string.Empty;
                    }
                }

                // Try to find any string property
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        property.Name != "id" && property.Name != "model")
                    {
                        return property.Value.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch (JsonException ex)
            {
                Logger.LogError(ex, "Failed to parse DeepAI JSON response");
                throw new FormatException("Invalid DeepAI response format");
            }
        }

        private long CalculateTokenEstimate(string prompt, string response)
        {
            var promptTokens = prompt.Length / 4;
            var responseTokens = response.Length / 4;
            return promptTokens + responseTokens;
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("text-davinci-003-free"))
            {
                return 0m;
            }
            else if (model.Contains("text-davinci"))
            {
                var costPerToken = 0.000001m;
                return (inputTokens + outputTokens) * costPerToken;
            }
            else
            {
                var costPerToken = 0.0000005m;
                return (inputTokens + outputTokens) * costPerToken;
            }
        }

        public override bool ShouldFallback(Exception exception)
        {
            return AiProviderHelper.ShouldFallbackCommon(exception) || base.ShouldFallback(exception);
        }
    }
}