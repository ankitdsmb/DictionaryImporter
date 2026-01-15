// TogetherAiProvider.cs (Enhanced version - Fixed)
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("TogetherAI", Priority = 5, SupportsCaching = true)]
public class TogetherAiProvider : EnhancedBaseProvider
{
    private const string DefaultModel = "mistralai/Mixtral-8x7B-Instruct-v0.1";
    private const string BaseUrl = "https://api.together.xyz/v1/chat/completions";

    // Store the current request for token estimation
    private AiRequest _currentRequest;

    public override string ProviderName => "TogetherAI";
    public override int Priority => 5;
    public override ProviderType Type => ProviderType.TextCompletion;
    public override bool SupportsAudio => false;
    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public TogetherAiProvider(
        HttpClient httpClient,
        ILogger<TogetherAiProvider> logger,
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
            Logger.LogWarning("TogetherAI API key not configured. Provider will be disabled.");
            Configuration.IsEnabled = false;
            return;
        }
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
        Capabilities.ChatCompletion = true;
        Capabilities.MaxTokensLimit = 8192;
        Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "ja", "ko", "zh" });
    }

    protected override void ConfigureAuthentication()
    {
        var apiKey = GetApiKey();
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
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
            // Store the current request for token estimation
            _currentRequest = request;

            // Check if provider is enabled
            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("TogetherAI provider is disabled");
            }

            // Check quota
            var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
            if (!quotaCheck.CanProceed)
            {
                throw new ProviderQuotaExceededException(ProviderName,
                    $"Quota exceeded. Remaining: {quotaCheck.RemainingRequests} requests, " +
                    $"{quotaCheck.RemainingTokens} tokens. Resets in {quotaCheck.TimeUntilReset.TotalMinutes:F0} minutes.");
            }

            // Check cache
            if (Configuration.EnableCaching)
            {
                var cachedResponse = await TryGetCachedResponseAsync(request);
                if (cachedResponse != null)
                {
                    return cachedResponse;
                }
            }

            // Validate request
            ValidateRequest(request);

            // Create payload
            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to TogetherAI with model {Model}", model);

            // Send request with resilience
            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Parse response
            var result = ParseResponse(content, out var tokenUsage);
            stopwatch.Stop();

            // Create response
            var aiResponse = new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = model,
                TokensUsed = tokenUsage,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                EstimatedCost = EstimateCost(tokenUsage, 0),
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["tokens_used"] = tokenUsage,
                    ["estimated_cost"] = EstimateCost(tokenUsage, 0),
                    ["together_ai"] = true,
                    ["open_source_models"] = true
                }
            };

            // Record usage
            await RecordUsageAsync(request, aiResponse, stopwatch.Elapsed, request.Context?.UserId);

            // Cache response
            if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
            {
                await CacheResponseAsync(
                    request,
                    aiResponse,
                    TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
            }

            return aiResponse;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "TogetherAI provider failed for request {RequestId}", request.Context?.RequestId);

            if (ShouldFallback(ex))
            {
                throw;
            }

            var errorResponse = new AiResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                Model = Configuration.Model ?? DefaultModel,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = false,
                ErrorCode = GetErrorCode(ex),
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = Configuration.Model ?? DefaultModel,
                    ["error_type"] = ex.GetType().Name,
                    ["stack_trace"] = ex.StackTrace
                }
            };

            // Record failed usage
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
            // Clear the stored request
            _currentRequest = null;
        }
    }

    private void ValidateRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt cannot be empty");

        if (request.MaxTokens > Capabilities.MaxTokensLimit)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds TogetherAI limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, Capabilities.MaxTokensLimit, Capabilities.MaxTokensLimit);
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        var messages = new List<object>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        messages.Add(new { role = "user", content = request.Prompt });

        return new
        {
            model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            messages = messages,
            max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
            temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
            top_p = 0.9,
            stream = false,
            stop = Array.Empty<string>()
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var url = Configuration.BaseUrl ?? BaseUrl;

        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonSerializerOptions),
                Encoding.UTF8,
                "application/json")
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
            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                {
                    throw new ProviderQuotaExceededException(ProviderName, $"TogetherAI error: {errorMessage}");
                }
                throw new HttpRequestException($"TogetherAI API error: {errorMessage}");
            }

            // Extract token usage
            if (root.TryGetProperty("usage", out var usage))
            {
                tokenUsage = usage.GetProperty("total_tokens").GetInt64();
            }
            else
            {
                // Estimate token usage if not provided
                if (_currentRequest != null)
                {
                    tokenUsage = EstimateTokenUsage(_currentRequest.Prompt);
                }
            }

            // Extract response content
            if (root.TryGetProperty("choices", out var choices))
            {
                var firstChoice = choices.EnumerateArray().FirstOrDefault();
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    var resultText = message.GetProperty("content").GetString() ?? string.Empty;

                    // Update token usage estimate if needed
                    if (tokenUsage == 0 && _currentRequest != null)
                    {
                        tokenUsage = EstimateTokenUsage(_currentRequest.Prompt) + EstimateTokenUsage(resultText);
                    }

                    return resultText;
                }
            }

            throw new FormatException("Could not find choices in TogetherAI response");
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse TogetherAI JSON response");
            throw new FormatException("Invalid TogetherAI response format");
        }
    }

    private string GetErrorCode(Exception ex)
    {
        return ex switch
        {
            ProviderQuotaExceededException => "QUOTA_EXCEEDED",
            RateLimitExceededException => "RATE_LIMIT_EXCEEDED",
            HttpRequestException httpEx => httpEx.StatusCode.HasValue ? $"HTTP_{httpEx.StatusCode.Value}" : "HTTP_ERROR",
            TimeoutException => "TIMEOUT",
            JsonException => "INVALID_RESPONSE",
            FormatException => "INVALID_RESPONSE",
            ArgumentException => "INVALID_REQUEST",
            _ => "UNKNOWN_ERROR"
        };
    }

    protected override decimal EstimateCost(long inputTokens, long outputTokens)
    {
        // Together AI pricing (approximate, should be updated with actual rates)
        var model = Configuration.Model ?? DefaultModel;

        if (model.Contains("llama-2-70b") || model.Contains("mixtral-8x7b"))
        {
            // Premium models pricing
            var inputCostPerToken = 0.0000009m;   // $0.90 per 1M tokens
            var outputCostPerToken = 0.0000009m;  // $0.90 per 1M tokens
            return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
        }
        else if (model.Contains("codellama") || model.Contains("mistral"))
        {
            // Mid-tier models pricing
            var inputCostPerToken = 0.0000003m;   // $0.30 per 1M tokens
            var outputCostPerToken = 0.0000003m;  // $0.30 per 1M tokens
            return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
        }
        else
        {
            // Default estimation
            var inputCostPerToken = 0.0000002m;   // $0.20 per 1M tokens
            var outputCostPerToken = 0.0000002m;  // $0.20 per 1M tokens
            return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
        }
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
                   message.Contains("insufficient_credits");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}