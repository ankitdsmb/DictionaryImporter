using Azure.Core;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("Watson", Priority = 13, SupportsCaching = true)]
public class WatsonProvider : EnhancedBaseProvider
{
    private const string DefaultModel = "ibm/granite-13b-chat-v2";
    private const string BaseUrl = "https://us-south.ml.cloud.ibm.com/ml/v1/text/generation?version=2023-05-29";

    // Store the current request for token estimation
    private AiRequest _currentRequest;

    public override string ProviderName => "Watson";
    public override int Priority => 13;
    public override ProviderType Type => ProviderType.TextCompletion;
    public override bool SupportsAudio => false;
    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public WatsonProvider(
        HttpClient httpClient,
        ILogger<WatsonProvider> logger,
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
            Logger.LogWarning("Watson (IBM) API key not configured. Provider will be disabled.");
            Configuration.IsEnabled = false;
            return;
        }
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
        Capabilities.MaxTokensLimit = 4096;
        Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "pt", "nl", "ja", "ko", "zh" });
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

            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("Watson provider is disabled");
            }

            var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
            if (!quotaCheck.CanProceed)
            {
                throw new ProviderQuotaExceededException(ProviderName,
                    $"Quota exceeded. Remaining: {quotaCheck.RemainingRequests} requests, " +
                    $"{quotaCheck.RemainingTokens} tokens. Resets in {quotaCheck.TimeUntilReset.TotalMinutes:F0} minutes.");
            }

            if (Configuration.EnableCaching)
            {
                var cachedResponse = await TryGetCachedResponseAsync(request);
                if (cachedResponse != null)
                {
                    return cachedResponse;
                }
            }

            ValidateRequest(request);

            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to Watson (IBM) with model {Model}", model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = ParseResponse(content, out var tokenUsage);
            stopwatch.Stop();

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
                    ["ibm_watson"] = true,
                    ["enterprise_grade"] = true
                }
            };

            await RecordUsageAsync(request, aiResponse, stopwatch.Elapsed, request.Context?.UserId);

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
            Logger.LogError(ex, "Watson provider failed for request {RequestId}", request.Context?.RequestId);

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
                "Requested {Requested} tokens exceeds Watson limit of {Limit}. Using {Limit} instead.",
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
            model_id = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            input = request.Prompt,
            parameters = new
            {
                max_new_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                top_p = 0.9,
                top_k = 50,
                repetition_penalty = 1.0,
                min_new_tokens = 1,
                random_seed = new Random().Next()
            },
            moderations = new
            {
                hap = new { input = true, output = true },
                custom = new { input = true, output = true }
            }
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
            if (root.TryGetProperty("errors", out var errorsElement))
            {
                var errorArray = errorsElement.EnumerateArray();
                if (errorArray.Any())
                {
                    var firstError = errorArray.First();
                    var errorMessage = firstError.GetProperty("message").GetString() ?? "Unknown error";

                    if (errorMessage.Contains("quota") || errorMessage.Contains("limit") ||
                        errorMessage.Contains("exceeded") || errorMessage.Contains("insufficient"))
                    {
                        throw new ProviderQuotaExceededException(ProviderName, $"Watson error: {errorMessage}");
                    }
                    throw new HttpRequestException($"Watson API error: {errorMessage}");
                }
            }

            // Extract response content
            if (root.TryGetProperty("results", out var results))
            {
                var firstResult = results.EnumerateArray().FirstOrDefault();
                if (firstResult.TryGetProperty("generated_text", out var generatedText))
                {
                    var resultText = generatedText.GetString() ?? string.Empty;

                    // Estimate token usage using the stored request
                    if (_currentRequest != null)
                    {
                        tokenUsage = EstimateTokenUsage(_currentRequest.Prompt) + EstimateTokenUsage(resultText);
                    }
                    else
                    {
                        tokenUsage = EstimateTokenUsage(resultText);
                    }

                    return resultText;
                }
            }

            throw new FormatException("Could not find results in Watson response");
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse Watson JSON response");
            throw new FormatException("Invalid Watson response format");
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
        var model = Configuration.Model ?? DefaultModel;

        if (model.Contains("granite-13b") || model.Contains("granite-20b"))
        {
            var costPerToken = 0.0000015m;
            return (inputTokens + outputTokens) * costPerToken;
        }
        else if (model.Contains("llama") || model.Contains("mistral"))
        {
            var costPerToken = 0.000001m;
            return (inputTokens + outputTokens) * costPerToken;
        }
        else
        {
            var costPerToken = 0.000002m;
            return (inputTokens + outputTokens) * costPerToken;
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
                   message.Contains("insufficient_credits") ||
                   message.Contains("exceeded_capacity");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}