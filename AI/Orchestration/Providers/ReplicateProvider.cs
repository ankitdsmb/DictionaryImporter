using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("Replicate", Priority = 12, SupportsCaching = true)]
public class ReplicateProvider : EnhancedBaseProvider
{
    private const string DefaultModel = "meta/llama-2-70b-chat";
    private const string BaseUrl = "https://api.replicate.com/v1/predictions";

    public override string ProviderName => "Replicate";
    public override int Priority => 12;
    public override ProviderType Type => ProviderType.TextCompletion;
    public override bool SupportsAudio => false;
    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public ReplicateProvider(
        HttpClient httpClient,
        ILogger<ReplicateProvider> logger,
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
            Logger.LogWarning("Replicate API key not configured. Provider will be disabled.");
            Configuration.IsEnabled = false;
            return;
        }
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
        Capabilities.MaxTokensLimit = 500;
        Capabilities.SupportedLanguages.Add("en");
    }

    protected override void ConfigureAuthentication()
    {
        var apiKey = GetApiKey();
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Token {apiKey}");
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
                throw new InvalidOperationException("Replicate provider is disabled");
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

            var predictionId = await CreatePredictionAsync(request, cancellationToken);

            var result = await PollPredictionAsync(predictionId, cancellationToken);
            stopwatch.Stop();

            var tokenUsage = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            var aiResponse = new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                TokensUsed = tokenUsage,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                EstimatedCost = EstimateCost(tokenUsage, 0),
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                    ["tokens_used"] = tokenUsage,
                    ["estimated_cost"] = EstimateCost(tokenUsage, 0),
                    ["replicate"] = true,
                    ["open_source"] = true,
                    ["prediction_id"] = predictionId,
                    ["processing_time_seconds"] = stopwatch.Elapsed.TotalSeconds
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
            Logger.LogError(ex, "Replicate provider failed for request {RequestId}", request.Context?.RequestId);

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

        if (request.MaxTokens > Capabilities.MaxTokensLimit)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds Replicate free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, Capabilities.MaxTokensLimit, Capabilities.MaxTokensLimit);
        }
    }

    private async Task<string> CreatePredictionAsync(AiRequest request, CancellationToken cancellationToken)
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

        var url = Configuration.BaseUrl ?? BaseUrl;
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonSerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        var response = await SendWithResilienceAsync(
            () => HttpClient.SendAsync(httpRequest, cancellationToken),
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        using var jsonDoc = JsonDocument.Parse(content);
        return jsonDoc.RootElement.GetProperty("id").GetString() ??
               throw new InvalidOperationException("No prediction ID received");
    }

    private async Task<string> PollPredictionAsync(string predictionId, CancellationToken cancellationToken)
    {
        var baseUrl = Configuration.BaseUrl ?? BaseUrl;
        var pollUrl = $"{baseUrl}/{predictionId}";

        var maxAttempts = 60;
        var attempt = 0;

        while (attempt < maxAttempts)
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
            attempt++;
        }

        throw new TimeoutException("Replicate prediction timeout");
    }

    private string GetModelVersion()
    {
        var modelVersions = new Dictionary<string, string>
        {
            ["meta/llama-2-70b-chat"] = "02e509c789964a7ea8736978a43525956ef40397be9033abf9fd2badfe68c9e3",
            ["mistralai/mistral-7b-instruct-v0.1"] = "5fe0a3d7ac2852264a25279d1dfb798acbc4d49711d126646594e212cb821749",
            ["google/flan-t5-xxl"] = "b7a93e2e1c9542794c5c0b6d7a78ef59d672b5c5b0c4c5c5f5a5c5d5e5f5a5b5c"
        };

        var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
        return modelVersions.GetValueOrDefault(model, "02e509c789964a7ea8736978a43525956ef40397be9033abf9fd2badfe68c9e3");
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
        else
        {
            var seconds = (inputTokens + outputTokens) / 150;
            var costPerSecond = 0.0083m;
            return seconds * costPerSecond;
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
                   message.Contains("monthly") ||
                   message.Contains("free tier") ||
                   message.Contains("credit");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}