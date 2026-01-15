using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("NLPCloud", Priority = 16, SupportsCaching = true)]
public class NlpCloudProvider : EnhancedBaseProvider
{
    private const string DefaultModel = "finetuned-llama-2-70b";
    private const string BaseUrl = "https://api.nlpcloud.io/v1/gpu/finetuned-llama-2-70b/generation";

    private AiRequest _currentRequest;

    public override string ProviderName => "NLPCloud";
    public override int Priority => 16;
    public override ProviderType Type => ProviderType.TextCompletion;
    public override bool SupportsAudio => false;
    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public NlpCloudProvider(
        HttpClient httpClient,
        ILogger<NlpCloudProvider> logger,
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
            Logger.LogWarning("NLP Cloud API key not configured. Provider will be disabled.");
            Configuration.IsEnabled = false;
            return;
        }
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
        Capabilities.MaxTokensLimit = 1024;
        Capabilities.SupportedLanguages.AddRange(new[] { "en", "fr", "de", "es", "it", "nl", "pt" });
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
            _currentRequest = request;
            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("NLP Cloud provider is disabled");
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

            Logger.LogDebug("Sending request to NLP Cloud with model {Model}", model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = ParseResponse(content);
            stopwatch.Stop();

            var tokenUsage = EstimateTokenUsage(request.Prompt) + EstimateTokenUsage(result);

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
                    ["nlp_cloud"] = true,
                    ["gpu_accelerated"] = true
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
            Logger.LogError(ex, "NLP Cloud provider failed for request {RequestId}", request.Context?.RequestId);

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
                "Requested {Requested} tokens exceeds NLP Cloud limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, Capabilities.MaxTokensLimit, Capabilities.MaxTokensLimit);
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        return new
        {
            text = request.Prompt,
            model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            max_length = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
            temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
            top_p = 0.9,
            top_k = 50,
            repetition_penalty = 1.0,
            num_return_sequences = 1,
            bad_words = Array.Empty<string>(),
            remove_input = false
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var url = Configuration.BaseUrl ?? BaseUrl;

        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }),
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

            if (root.TryGetProperty("detail", out var detailElement))
            {
                var errorMessage = detailElement.GetString() ?? "Unknown error";
                if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                {
                    throw new ProviderQuotaExceededException(ProviderName, $"NLP Cloud error: {errorMessage}");
                }
                throw new HttpRequestException($"NLP Cloud API error: {errorMessage}");
            }

            if (root.TryGetProperty("generated_text", out var generatedText))
            {
                var resultText = generatedText.GetString() ?? string.Empty;

                if (_currentRequest != null)
                {
                    return resultText;
                }
                return resultText;
            }

            throw new FormatException("Could not find generated_text in NLP Cloud response");
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse NLP Cloud JSON response");
            throw new FormatException("Invalid NLP Cloud response format");
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

        if (model.Contains("finetuned-llama-2-70b"))
        {
            var costPerToken = 0.0000025m;
            return (inputTokens + outputTokens) * costPerToken;
        }
        else if (model.Contains("gpt-j") || model.Contains("neo"))
        {
            var costPerToken = 0.0000005m;
            return (inputTokens + outputTokens) * costPerToken;
        }
        else
        {
            var costPerToken = 0.000001m;
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
                   message.Contains("free tier");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}