using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("Gemini", Priority = 3, SupportsCaching = true)]
public class GeminiProvider : EnhancedBaseProvider
{
    private const string DefaultModel = "gemini-pro";
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    public override string ProviderName => "Gemini";
    public override int Priority => 3;
    public override ProviderType Type => ProviderType.TextCompletion;
    public override bool SupportsAudio => false;
    public override bool SupportsVision => true;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public GeminiProvider(
        HttpClient httpClient,
        ILogger<GeminiProvider> logger,
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
            Logger.LogWarning("Gemini API key not configured. Provider will be disabled.");
            Configuration.IsEnabled = false;
            return;
        }
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
        Capabilities.ImageAnalysis = true;
        Capabilities.MaxTokensLimit = 32768;
        Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "ja", "ko", "zh" });
    }

    protected override void ConfigureAuthentication()
    {
        HttpClient.DefaultRequestHeaders.Clear();
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
                throw new InvalidOperationException("Gemini provider is disabled");
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

            Logger.LogDebug("Sending request to Gemini with model {Model}", model);

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
                    ["google_ai"] = true,
                    ["supports_vision"] = true,
                    ["supports_multimodal"] = request.ImageData != null || request.ImageUrls?.Count > 0
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
            Logger.LogError(ex, "Gemini provider failed for request {RequestId}", request.Context?.RequestId);

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
                "Requested {Requested} tokens exceeds Gemini limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, Capabilities.MaxTokensLimit, Capabilities.MaxTokensLimit);
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

        if (request.ImageData != null || request.ImageUrls?.Count > 0)
        {
            return CreateVisionPayload(request, model);
        }

        var contents = new List<object>
        {
            new {
                parts = new[] {
                    new { text = request.Prompt }
                }
            }
        };

        return new
        {
            contents = contents,
            generationConfig = new
            {
                maxOutputTokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                topP = 0.95,
                topK = 40
            },
            safetySettings = new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" }
            }
        };
    }

    private object CreateVisionPayload(AiRequest request, string model)
    {
        var parts = new List<object> { new { text = request.Prompt } };

        if (request.ImageData != null)
        {
            var base64Image = Convert.ToBase64String(request.ImageData);
            parts.Add(new
            {
                inlineData = new
                {
                    mimeType = GetMimeType(request.ImageFormat),
                    data = base64Image
                }
            });
        }
        else if (request.ImageUrls?.Count > 0)
        {
            foreach (var url in request.ImageUrls.Take(1))
            {
                parts.Add(new
                {
                    inlineData = new
                    {
                        mimeType = "image/jpeg",
                        data = GetBase64FromUrl(url)
                    }
                });
            }
        }

        return new
        {
            contents = new[] { new { parts = parts } },
            generationConfig = new
            {
                maxOutputTokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                topP = 0.95,
                topK = 40
            },
            safetySettings = new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" }
            }
        };
    }

    private string GetMimeType(string imageFormat)
    {
        if (string.IsNullOrEmpty(imageFormat))
            return "image/jpeg";

        return imageFormat.ToLower() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    private string GetBase64FromUrl(string url)
    {
        return string.Empty;
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
        var url = Configuration.BaseUrl ?? BaseUrl.Replace("{model}", model);
        url = $"{url}?key={Configuration.ApiKey}";

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

            if (root.TryGetProperty("error", out var error))
            {
                var errorMessage = error.GetProperty("message").GetString() ?? "Unknown error";
                if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                {
                    throw new ProviderQuotaExceededException(ProviderName, $"Gemini error: {errorMessage}");
                }
                throw new HttpRequestException($"Gemini API error: {errorMessage}");
            }

            if (root.TryGetProperty("usageMetadata", out var usageMetadata))
            {
                tokenUsage = usageMetadata.GetProperty("totalTokenCount").GetInt64();
            }
            else
            {
                if (root.TryGetProperty("candidates", out var candidates))
                {
                    var firstCandidate = candidates.EnumerateArray().FirstOrDefault();
                    if (firstCandidate.TryGetProperty("content", out var content))
                    {
                        if (content.TryGetProperty("parts", out var parts))
                        {
                            var firstPart = parts.EnumerateArray().FirstOrDefault();
                            if (firstPart.TryGetProperty("text", out var text))
                            {
                                var resultText = text.GetString() ?? string.Empty;
                                tokenUsage = EstimateTokenUsage(resultText);
                            }
                        }
                    }
                }
            }

            if (root.TryGetProperty("candidates", out var candidatesElement))
            {
                var candidateArray = candidatesElement.EnumerateArray();
                if (candidateArray.Any())
                {
                    var firstCandidate = candidateArray.First();
                    if (firstCandidate.TryGetProperty("content", out var content))
                    {
                        if (content.TryGetProperty("parts", out var parts))
                        {
                            var firstPart = parts.EnumerateArray().FirstOrDefault();
                            if (firstPart.TryGetProperty("text", out var text))
                            {
                                return text.GetString() ?? string.Empty;
                            }
                        }
                    }
                }
            }

            throw new FormatException("Could not find valid response content in Gemini response");
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse Gemini JSON response");
            throw new FormatException("Invalid Gemini response format");
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

        if (model.Contains("gemini-1.5") || model.Contains("gemini-pro"))
        {
            var inputCostPerToken = 0.000000125m;
            var outputCostPerToken = 0.000000375m;
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
                   message.Contains("resource exhausted");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}