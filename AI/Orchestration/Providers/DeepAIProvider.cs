using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

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
        var apiKey = GetApiKey();
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("api-key", apiKey);
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
            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("DeepAI provider is disabled");
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

            Logger.LogDebug("Sending request to DeepAI with model {Model}", model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = ParseResponse(content);
            stopwatch.Stop();

            var tokenUsage = CalculateTokenEstimate(request.Prompt, result);

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
                    ["deepai"] = true,
                    ["free_tier_max_tokens"] = 300
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
            Logger.LogError(ex, "DeepAI provider failed for request {RequestId}", request.Context?.RequestId);

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

        if (request.Prompt.Length > 4000)
            throw new ArgumentException($"Prompt exceeds DeepAI limit of 4000 characters. Length: {request.Prompt.Length}");

        if (request.MaxTokens > Capabilities.MaxTokensLimit)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds DeepAI free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, Capabilities.MaxTokensLimit, Capabilities.MaxTokensLimit);
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

    private string ParseResponse(string jsonResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("err", out var errElement))
            {
                var errorMessage = errElement.GetString() ?? "Unknown error";
                throw new ProviderQuotaExceededException(ProviderName, $"DeepAI error: {errorMessage}");
            }

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

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Name != "id" &&
                    property.Name != "model")
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
                   message.Contains("free tier") ||
                   message.Contains("insufficient credits");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}