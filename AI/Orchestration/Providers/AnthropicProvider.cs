using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Helpers;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("Anthropic", Priority = 4, SupportsCaching = true)]
    public class AnthropicProvider : EnhancedBaseProvider
    {
        private const string DefaultModel = "claude-3-haiku-20240307";
        private const string BaseUrl = "https://api.anthropic.com/v1/messages";

        public override string ProviderName => "Anthropic";
        public override int Priority => 4;
        public override ProviderType Type => ProviderType.TextCompletion;
        public override bool SupportsAudio => false;
        public override bool SupportsVision => true;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public AnthropicProvider(
            HttpClient httpClient,
            ILogger<AnthropicProvider> logger,
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
                Logger.LogWarning("Anthropic API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.ImageAnalysis = true;
            Capabilities.MaxTokensLimit = 4096;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            var apiKey = GetApiKey();
            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            HttpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            HttpClient.DefaultRequestHeaders.Add("anthropic-beta", "max-tokens-2024-07-15");
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Validate request
                AiProviderHelper.ValidateCommonRequest(request, Capabilities, Logger);

                // Check if provider is enabled
                if (!Configuration.IsEnabled)
                    throw new InvalidOperationException("Anthropic provider is disabled");

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

                Logger.LogDebug("Sending request to Anthropic with model {Model}", model);

                // Create and send request
                var httpRequest = AiProviderHelper.CreateJsonRequestWithSnakeCase(payload, url);
                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                // Parse response
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseResponse(content, out var inputTokens, out var outputTokens);

                stopwatch.Stop();

                // Create success response
                var aiResponse = AiProviderHelper.CreateSuccessResponse(
                    result,
                    ProviderName,
                    model,
                    inputTokens + outputTokens,
                    stopwatch.Elapsed,
                    EstimateCost(inputTokens, outputTokens),
                    new Dictionary<string, object>
                    {
                        ["input_tokens"] = inputTokens,
                        ["output_tokens"] = outputTokens,
                        ["total_tokens"] = inputTokens + outputTokens,
                        ["anthropic"] = true,
                        ["supports_vision"] = true
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
                Logger.LogError(ex, "Anthropic provider failed for request {RequestId}", request.Context?.RequestId);

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
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            // Handle vision requests
            if (request.ImageData != null || request.ImageUrls?.Count > 0)
            {
                return CreateVisionPayload(request, model);
            }

            // Text-only request
            return new
            {
                model = model,
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new { type = "text", text = request.Prompt }
                        }
                    }
                },
                system = request.SystemPrompt ?? "You are a helpful AI assistant."
            };
        }

        private object CreateVisionPayload(AiRequest request, string model)
        {
            var content = new List<object>
            {
                new { type = "text", text = request.Prompt }
            };

            // Add image from data
            if (request.ImageData != null)
            {
                var base64Image = Convert.ToBase64String(request.ImageData);
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = AiProviderHelper.GetMimeType(request.ImageFormat),
                        data = base64Image
                    }
                });
            }
            // Add image from URL
            else if (request.ImageUrls?.Count > 0)
            {
                foreach (var url in request.ImageUrls.Take(1))
                {
                    content.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "url",
                            url = url,
                            media_type = "image/jpeg"
                        }
                    });
                }
            }

            return new
            {
                model = model,
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                messages = new[]
                {
                    new { role = "user", content = content }
                },
                system = request.SystemPrompt ?? "You are a helpful AI assistant."
            };
        }

        private string ParseResponse(string jsonResponse, out long inputTokens, out long outputTokens)
        {
            inputTokens = 0;
            outputTokens = 0;

            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);
                var root = jsonDoc.RootElement;

                // Check for errors
                if (AiProviderHelper.HasError(root, out var errorMessage))
                {
                    if (AiProviderHelper.IsQuotaError(errorMessage))
                        throw new ProviderQuotaExceededException(ProviderName, $"Anthropic error: {errorMessage}");
                    throw new HttpRequestException($"Anthropic API error: {errorMessage}");
                }

                // Extract token usage
                if (root.TryGetProperty("usage", out var usage))
                {
                    inputTokens = usage.GetProperty("input_tokens").GetInt64();
                    outputTokens = usage.GetProperty("output_tokens").GetInt64();
                }

                // Extract response text
                if (root.TryGetProperty("content", out var contentArray))
                {
                    var firstContent = contentArray.EnumerateArray().FirstOrDefault();
                    if (firstContent.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString() ?? string.Empty;
                    }
                }
                throw new FormatException("Could not find content in Anthropic response");
            }
            catch (JsonException ex)
            {
                Logger.LogError(ex, "Failed to parse Anthropic JSON response");
                throw new FormatException("Invalid Anthropic response format");
            }
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("claude-3-opus"))
            {
                var inputCostPerToken = 0.000015m;
                var outputCostPerToken = 0.000075m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("claude-3-sonnet"))
            {
                var inputCostPerToken = 0.000003m;
                var outputCostPerToken = 0.000015m;
                return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
            }
            else if (model.Contains("claude-3-haiku"))
            {
                var inputCostPerToken = 0.00000025m;
                var outputCostPerToken = 0.00000125m;
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