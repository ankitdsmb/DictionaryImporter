using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("AlephAlphaVision", Priority = 18, SupportsCaching = true)]
public class AlephAlphaVisionProvider : EnhancedBaseProvider
{
    private const string DefaultModel = "luminous-base";
    private const string BaseUrl = "https://api.aleph-alpha.com/complete";

    public override string ProviderName => "AlephAlphaVision";
    public override int Priority => 18;
    public override ProviderType Type => ProviderType.VisionAnalysis;
    public override bool SupportsAudio => false;
    public override bool SupportsVision => true;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public AlephAlphaVisionProvider(
        HttpClient httpClient,
        ILogger<AlephAlphaVisionProvider> logger,
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
        Capabilities.ImageAnalysis = true;
        Capabilities.MaxTokensLimit = 2048;
        Capabilities.SupportedLanguages.Add("en");
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
            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("Aleph Alpha Vision provider is disabled");
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

            if (!IsImageRequest(request))
            {
                return await HandleTextCompletionAsync(request, cancellationToken);
            }

            ValidateImageRequest(request);

            var payload = CreateMultimodalPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending multimodal request to Aleph Alpha Vision with model {Model}", model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = ParseVisionResponse(content);
            stopwatch.Stop();

            var tokenUsage = EstimateVisionTokenUsage(request, result);

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
                    ["aleph_alpha"] = true,
                    ["vision_capabilities"] = true,
                    ["multimodal"] = true,
                    ["european_data_center"] = true
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
            Logger.LogError(ex, "Aleph Alpha Vision provider failed for request {RequestId}", request.Context?.RequestId);

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

    private bool IsImageRequest(AiRequest request)
    {
        return request.AdditionalParameters?.ContainsKey("image_url") == true ||
               request.AdditionalParameters?.ContainsKey("image_base64") == true ||
               (request.Prompt?.Contains("data:image/") == true && request.Prompt.Contains("base64"));
    }

    private async Task<AiResponse> HandleTextCompletionAsync(
        AiRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            prompt = request.Prompt,
            maximum_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
            temperature = Math.Clamp(request.Temperature, 0.0, 1.0)
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
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
        var result = ParseTextResponse(content);

        return new AiResponse
        {
            Content = result.Trim(),
            Provider = ProviderName,
            TokensUsed = EstimateTextTokenUsage(request.Prompt, result),
            ProcessingTime = TimeSpan.Zero,
            IsSuccess = true,
            Metadata = new Dictionary<string, object>
            {
                ["model"] = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                ["text_only"] = true
            }
        };
    }

    private void ValidateImageRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) &&
            !(request.AdditionalParameters?.ContainsKey("image_url") == true ||
              request.AdditionalParameters?.ContainsKey("image_base64") == true))
        {
            throw new ArgumentException("Either prompt or image data required for vision analysis");
        }
    }

    private object CreateMultimodalPayload(AiRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            ["prompt"] = request.Prompt ?? "Describe this image",
            ["maximum_tokens"] = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
            ["temperature"] = Math.Clamp(request.Temperature, 0.0, 1.0)
        };

        if (request.AdditionalParameters != null)
        {
            if (request.AdditionalParameters.TryGetValue("image_url", out var imageUrl))
            {
                payload["image_url"] = imageUrl;
            }
            else if (request.AdditionalParameters.TryGetValue("image_base64", out var imageBase64))
            {
                payload["image_base64"] = imageBase64;
            }
        }

        return payload;
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var url = Configuration.BaseUrl ?? BaseUrl;

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonSerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Add("Content-Type", "application/json");
        return request;
    }

    private string ParseVisionResponse(string jsonResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                {
                    throw new ProviderQuotaExceededException(ProviderName, $"Aleph Alpha Vision error: {errorMessage}");
                }
                throw new HttpRequestException($"Aleph Alpha Vision API error: {errorMessage}");
            }

            if (root.TryGetProperty("completions", out var completions))
            {
                var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                if (firstCompletion.TryGetProperty("completion", out var completion))
                {
                    return completion.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find completions in Aleph Alpha Vision response");
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse Aleph Alpha Vision JSON response");
            throw new FormatException("Invalid Aleph Alpha Vision response format");
        }
    }

    private string ParseTextResponse(string jsonResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            if (jsonDoc.RootElement.TryGetProperty("completions", out var completions))
            {
                var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                if (firstCompletion.TryGetProperty("completion", out var completion))
                {
                    return completion.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse Aleph Alpha text response");
            return string.Empty;
        }
    }

    private long EstimateVisionTokenUsage(AiRequest request, string result)
    {
        var baseTokens = ((request.Prompt?.Length ?? 0) + result.Length) / 4;
        return baseTokens + 1000;
    }

    private long EstimateTextTokenUsage(string prompt, string result)
    {
        return (prompt.Length + result.Length) / 4;
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

        if (model.Contains("luminous-extended"))
        {
            var inputCostPerToken = 0.00001m;
            var outputCostPerToken = 0.00002m;
            return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
        }
        else if (model.Contains("luminous-supreme"))
        {
            var inputCostPerToken = 0.000015m;
            var outputCostPerToken = 0.00003m;
            return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
        }
        else
        {
            var inputCostPerToken = 0.000005m;
            var outputCostPerToken = 0.00001m;
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
                   message.Contains("monthly") ||
                   message.Contains("free tier") ||
                   message.Contains("vision");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}