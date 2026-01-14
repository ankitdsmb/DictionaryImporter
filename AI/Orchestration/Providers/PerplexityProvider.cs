using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class PerplexityProvider : BaseCompletionProvider
{
    private const string DefaultModel = "sonar-small-online";
    private const int FreeTierMaxTokens = 4000;
    private const int FreeTierRequestsPerDay = 50;

    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object DailyCounterLock = new();

    public override string ProviderName => "Perplexity";
    public override int Priority => 9;
    public override ProviderType Type => ProviderType.TextCompletion;

    public override bool SupportsAudio => false;

    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public PerplexityProvider(
        HttpClient httpClient,
        ILogger<PerplexityProvider> logger,
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("Perplexity API key not configured. Provider will be disabled.");
            return;
        }
        ConfigureAuthentication();
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
        Capabilities.ChatCompletion = true;
        Capabilities.MaxTokensLimit = FreeTierMaxTokens;
        Capabilities.SupportedLanguages.Add("en");
    }

    protected override void ConfigureAuthentication()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {Configuration.ApiKey}");
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
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                throw new InvalidOperationException("Perplexity API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new PerplexityQuotaExceededException(
                    $"Perplexity free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
            }

            ValidateRequest(request);
            IncrementDailyCount();

            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to Perplexity with model {Model}", model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = ParseResponse(content, out var tokenUsage);

            stopwatch.Stop();

            return new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = model,
                TokensUsed = tokenUsage,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "free_tier", true },
                        { "daily_requests_used", GetDailyRequestCount() },
                        { "daily_requests_remaining", FreeTierRequestsPerDay - GetDailyRequestCount() },
                        { "web_search_enabled", model.Contains("online") }
                    }
            };
        }
        catch (PerplexityQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Perplexity provider failed");
            if (ShouldFallback(ex)) throw;

            return new AiResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                        { "error_type", ex.GetType().Name }
                    }
            };
        }
    }

    private bool CheckDailyLimit()
    {
        lock (DailyCounterLock)
        {
            if (DateTime.UtcNow.Date > _lastResetDate)
            {
                _dailyRequestCount = 0;
                _lastResetDate = DateTime.UtcNow.Date;
            }
            return _dailyRequestCount < FreeTierRequestsPerDay;
        }
    }

    private void IncrementDailyCount()
    {
        lock (DailyCounterLock)
        {
            _dailyRequestCount++;
        }
    }

    private long GetDailyRequestCount()
    {
        lock (DailyCounterLock)
        {
            if (DateTime.UtcNow.Date > _lastResetDate)
            {
                _dailyRequestCount = 0;
                _lastResetDate = DateTime.UtcNow.Date;
            }
            return _dailyRequestCount;
        }
    }

    private void ValidateRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt cannot be empty");

        if (request.MaxTokens > FreeTierMaxTokens)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds Perplexity free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
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
            max_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
            temperature = Math.Clamp(request.Temperature, 0.0, 2.0),
            top_p = 0.9,
            stream = false,
            search_domain_filter = Array.Empty<string>(),
            return_images = false,
            return_related_questions = false
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.perplexity.ai/chat/completions" : Configuration.BaseUrl;

        return new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }),
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

            if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                    throw new PerplexityQuotaExceededException($"Perplexity quota exceeded: {errorMessage}");
                throw new HttpRequestException($"Perplexity API error: {errorMessage}");
            }

            if (jsonDoc.RootElement.TryGetProperty("usage", out var usage))
            {
                tokenUsage = usage.GetProperty("total_tokens").GetInt64();
            }

            if (jsonDoc.RootElement.TryGetProperty("choices", out var choices))
            {
                var firstChoice = choices.EnumerateArray().FirstOrDefault();
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    return message.GetProperty("content").GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find choices in Perplexity response");
        }
        catch (PerplexityQuotaExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse Perplexity response");
            throw new FormatException("Invalid Perplexity response format");
        }
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is PerplexityQuotaExceededException)
            return true;

        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("daily") ||
                   message.Contains("free tier");
        }

        return base.ShouldFallback(exception);
    }
}