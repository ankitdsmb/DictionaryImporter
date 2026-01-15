using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("OpenRouter", Priority = 1, SupportsCaching = true)]
public class OpenRouterProvider : EnhancedBaseProvider
{
    private const string DefaultModel = "openai/gpt-3.5-turbo";
    private const string BaseUrl = "https://api.openrouter.ai/api/v1/chat/completions";

    private static readonly ConcurrentDictionary<string, DateTime> _requestTimestamps = new();
    private static readonly object _rateLimitLock = new();

    public override string ProviderName => "OpenRouter";
    public override int Priority => 1;
    public override ProviderType Type => ProviderType.TextCompletion;
    public override bool SupportsAudio => false;
    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public OpenRouterProvider(
        HttpClient httpClient,
        ILogger<OpenRouterProvider> logger,
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
            Logger.LogWarning("OpenRouter API key not configured. Provider will be disabled.");
            Configuration.IsEnabled = false;
            return;
        }
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        _capabilities.ChatCompletion = true;
        _capabilities.MaxTokensLimit = 4096;
        _capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "ja", "ko", "zh" });
    }

    protected override void ConfigureAuthentication()
    {
        var apiKey = GetApiKey();

        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        HttpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://dictionary-importer.com");
        HttpClient.DefaultRequestHeaders.Add("X-Title", "Dictionary Importer");
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public override async Task<AiResponse> GetCompletionAsync(
        AiRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("OpenRouter provider is disabled");
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

            await ApplyRateLimitingAsync();

            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);

            Logger.LogDebug("Sending request to OpenRouter with model {Model}", Configuration.Model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = ParseResponse(content, out var tokenUsage, out var modelUsed);

            stopwatch.Stop();

            var aiResponse = new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = modelUsed,
                TokensUsed = tokenUsage.TotalTokens,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                EstimatedCost = CalculateCost(tokenUsage),
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = modelUsed,
                    ["input_tokens"] = tokenUsage.InputTokens,
                    ["output_tokens"] = tokenUsage.OutputTokens,
                    ["total_tokens"] = tokenUsage.TotalTokens,
                    ["estimated_cost"] = CalculateCost(tokenUsage),
                    ["openrouter"] = true,
                    ["api_version"] = "v1",
                    ["rate_limit_remaining"] = GetRemainingRequests()
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

            Logger.LogError(ex, "OpenRouter provider failed for request {RequestId}",
                request.Context?.RequestId);

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
    }

    private void ValidateRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt cannot be empty");

        if (request.Prompt.Length > 32000)
            throw new ArgumentException($"Prompt exceeds OpenRouter limit of 32,000 characters. Length: {request.Prompt.Length}");

        if (request.MaxTokens > 4096)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds OpenRouter limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, 4096, 4096);
        }
    }

    private async Task ApplyRateLimitingAsync()
    {
        if (!Configuration.EnableRateLimiting)
            return;

        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var minuteKey = now.ToString("yyyyMMddHHmm");

            var oldKeys = _requestTimestamps.Keys
                .Where(k => DateTime.ParseExact(k, "yyyyMMddHHmm", null) < now.AddMinutes(-5))
                .ToList();

            foreach (var key in oldKeys)
            {
                _requestTimestamps.TryRemove(key, out _);
            }

            var requestsThisMinute = _requestTimestamps.Count(kv =>
                DateTime.ParseExact(kv.Key, "yyyyMMddHHmm", null) >= now.AddMinutes(-1));

            var maxRequestsPerMinute = Configuration.RequestsPerMinute > 0
                ? Configuration.RequestsPerMinute : 60;

            if (requestsThisMinute >= maxRequestsPerMinute)
            {
                var nextMinute = now.AddMinutes(1);
                var waitTime = nextMinute - now;

                throw new RateLimitExceededException(
                    ProviderName,
                    waitTime,
                    $"Rate limit exceeded: {maxRequestsPerMinute} requests/minute. " +
                    $"Try again in {waitTime.TotalSeconds:F0} seconds.");
            }

            _requestTimestamps[minuteKey] = now;
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

        var payload = new Dictionary<string, object>
        {
            ["model"] = Configuration.Model ?? DefaultModel,
            ["messages"] = messages,
            ["max_tokens"] = Math.Min(request.MaxTokens, 4096),
            ["temperature"] = Math.Clamp(request.Temperature, 0.0, 2.0),
            ["top_p"] = 0.9,
            ["frequency_penalty"] = 0.0,
            ["presence_penalty"] = 0.0,
            ["stream"] = false
        };

        if (request.AdditionalParameters != null)
        {
            foreach (var param in request.AdditionalParameters)
            {
                if (!payload.ContainsKey(param.Key))
                {
                    payload[param.Key] = param.Value;
                }
            }
        }

        return payload;
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

    private string ParseResponse(string jsonResponse, out TokenUsage tokenUsage, out string modelUsed)
    {
        tokenUsage = new TokenUsage();
        modelUsed = Configuration.Model ?? DefaultModel;

        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                var errorType = errorElement.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : "unknown";

                if (errorType == "insufficient_quota" ||
                    errorType == "rate_limit_exceeded" ||
                    errorMessage.Contains("quota") ||
                    errorMessage.Contains("limit"))
                {
                    throw new ProviderQuotaExceededException(ProviderName, $"OpenRouter error: {errorMessage}");
                }

                throw new HttpRequestException($"OpenRouter API error: {errorMessage}");
            }

            if (root.TryGetProperty("usage", out var usageElement))
            {
                tokenUsage.InputTokens = usageElement.TryGetProperty("prompt_tokens", out var promptTokens)
                    ? promptTokens.GetInt64()
                    : 0;

                tokenUsage.OutputTokens = usageElement.TryGetProperty("completion_tokens", out var completionTokens)
                    ? completionTokens.GetInt64()
                    : 0;

                tokenUsage.TotalTokens = usageElement.TryGetProperty("total_tokens", out var totalTokens)
                    ? totalTokens.GetInt64()
                    : tokenUsage.InputTokens + tokenUsage.OutputTokens;
            }

            if (root.TryGetProperty("model", out var modelElement))
            {
                modelUsed = modelElement.GetString() ?? modelUsed;
            }

            if (root.TryGetProperty("choices", out var choicesElement))
            {
                var choices = choicesElement.EnumerateArray();
                if (choices.Any())
                {
                    var firstChoice = choices.First();
                    if (firstChoice.TryGetProperty("message", out var messageElement))
                    {
                        if (messageElement.TryGetProperty("content", out var contentElement))
                        {
                            return contentElement.GetString() ?? string.Empty;
                        }
                    }
                }
            }

            throw new FormatException("Could not find valid response content in OpenRouter response");
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse OpenRouter JSON response");
            throw new FormatException("Invalid OpenRouter response format");
        }
    }

    private decimal CalculateCost(TokenUsage usage)
    {
        var model = Configuration.Model ?? DefaultModel;
        decimal inputCostPerToken = 0.000001m;
        decimal outputCostPerToken = 0.000002m;

        if (model.Contains("gpt-4"))
        {
            inputCostPerToken = 0.00003m;
            outputCostPerToken = 0.00006m;
        }
        else if (model.Contains("gpt-3.5-turbo"))
        {
            inputCostPerToken = 0.0000015m;
            outputCostPerToken = 0.000002m;
        }

        return (usage.InputTokens * inputCostPerToken) + (usage.OutputTokens * outputCostPerToken);
    }

    private int GetRemainingRequests()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var requestsThisMinute = _requestTimestamps.Count(kv =>
                DateTime.ParseExact(kv.Key, "yyyyMMddHHmm", null) >= now.AddMinutes(-1));

            var maxRequestsPerMinute = Configuration.RequestsPerMinute > 0
                ? Configuration.RequestsPerMinute : 60;

            return Math.Max(0, maxRequestsPerMinute - requestsThisMinute);
        }
    }

    private string GetErrorCode(Exception ex)
    {
        return ex switch
        {
            ProviderQuotaExceededException => "QUOTA_EXCEEDED",
            RateLimitExceededException => "RATE_LIMIT_EXCEEDED",
            HttpRequestException httpEx => httpEx.StatusCode.HasValue
                ? $"HTTP_{httpEx.StatusCode.Value}"
                : "HTTP_ERROR",
            TimeoutException => "TIMEOUT",
            JsonException => "INVALID_RESPONSE",
            FormatException => "INVALID_RESPONSE",
            ArgumentException => "INVALID_REQUEST",
            _ => "UNKNOWN_ERROR"
        };
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is ProviderQuotaExceededException ||
            exception is RateLimitExceededException)
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
                   message.Contains("insufficient_quota") ||
                   message.Contains("insufficient credits") ||
                   message.Contains("billing") ||
                   message.Contains("payment required");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}

internal class TokenUsage
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }

    public long CalculatedTotalTokens => InputTokens + OutputTokens;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ProviderAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public int Priority { get; set; } = 10;
    public bool SupportsCaching { get; set; } = false;
}