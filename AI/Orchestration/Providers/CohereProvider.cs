using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;
using System.Globalization;

public class CohereProvider : BaseCompletionProvider
{
    private const string DefaultModel = "command-light";
    private const int FreeTierMaxTokens = 4000;
    private const int FreeTierRequestsPerMinute = 5;
    private const int FreeTierRequestsPerDay = 100;

    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object DailyCounterLock = new();

    private readonly ConcurrentDictionary<string, DateTime> _rateLimitTracker = new();
    private readonly object _rateLimitLock = new();

    public override string ProviderName => "Cohere";
    public override int Priority => 6;
    public override ProviderType Type => ProviderType.TextCompletion;

    public override bool SupportsAudio => false;

    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public CohereProvider(
        HttpClient httpClient,
        ILogger<CohereProvider> logger,
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("Cohere API key not configured. Provider will be disabled.");
            return;
        }
        ConfigureAuthentication();
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
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
                throw new InvalidOperationException("Cohere API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new CohereQuotaExceededException(
                    $"Cohere free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
            }

            CheckRateLimit();

            ValidateRequest(request);
            IncrementDailyCount();

            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to Cohere with model {Model}", model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = ParseResponse(content, out var tokenUsage);

            stopwatch.Stop();
            UpdateRateLimit();

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
                        { "rate_limit_remaining", GetRemainingRequests() }
                    }
            };
        }
        catch (CohereQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (CohereRateLimitException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Cohere provider failed");
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

    private void CheckRateLimit()
    {
        lock (_rateLimitLock)
        {
            var minuteKey = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            var currentMinute = DateTime.UtcNow;

            var oldKeys = _rateLimitTracker.Keys
                .Where(k => DateTime.ParseExact(k, "yyyyMMddHHmm", CultureInfo.InvariantCulture) < currentMinute.AddMinutes(-5))
                .ToList();

            foreach (var key in oldKeys)
                _rateLimitTracker.TryRemove(key, out _);

            var requestsThisMinute = _rateLimitTracker.Count(kv =>
                DateTime.ParseExact(kv.Key, "yyyyMMddHHmm", CultureInfo.InvariantCulture) >= currentMinute.AddMinutes(-1));

            if (requestsThisMinute >= FreeTierRequestsPerMinute)
            {
                var nextMinute = currentMinute.AddMinutes(1);
                var waitTime = nextMinute - currentMinute;
                throw new CohereRateLimitException(
                    $"Cohere free tier rate limit exceeded. {FreeTierRequestsPerMinute} requests/minute allowed. " +
                    $"Try again in {waitTime.TotalSeconds:F0} seconds.");
            }
        }
    }

    private void UpdateRateLimit()
    {
        var minuteKey = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        _rateLimitTracker[minuteKey] = DateTime.UtcNow;
    }

    private int GetRemainingRequests()
    {
        lock (_rateLimitLock)
        {
            var currentMinute = DateTime.UtcNow;
            var requestsThisMinute = _rateLimitTracker.Count(kv =>
                DateTime.ParseExact(kv.Key, "yyyyMMddHHmm", CultureInfo.InvariantCulture) >= currentMinute.AddMinutes(-1));
            return Math.Max(0, FreeTierRequestsPerMinute - requestsThisMinute);
        }
    }

    private void ValidateRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt cannot be empty");

        if (request.MaxTokens > FreeTierMaxTokens)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds Cohere free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        return new
        {
            model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            prompt = request.Prompt,
            max_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
            temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
            k = 0,
            p = 0.75,
            frequency_penalty = 0.0,
            presence_penalty = 0.0,
            stop_sequences = Array.Empty<string>(),
            return_likelihoods = "NONE"
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.cohere.ai/v1/generate" : Configuration.BaseUrl;

        return new HttpRequestMessage(HttpMethod.Post, baseUrl)
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

    private string ParseResponse(string jsonResponse, out long tokenUsage)
    {
        tokenUsage = 0;

        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            if (jsonDoc.RootElement.TryGetProperty("message", out var messageElement))
            {
                var errorMessage = messageElement.GetString() ?? "Unknown error";
                if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                    throw new CohereQuotaExceededException($"Cohere quota exceeded: {errorMessage}");
                throw new HttpRequestException($"Cohere API error: {errorMessage}");
            }

            if (jsonDoc.RootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("tokens", out var tokens))
            {
                if (tokens.TryGetProperty("input_tokens", out var inputTokens))
                    tokenUsage += inputTokens.GetInt64();
                if (tokens.TryGetProperty("output_tokens", out var outputTokens))
                    tokenUsage += outputTokens.GetInt64();
            }

            if (jsonDoc.RootElement.TryGetProperty("generations", out var generations))
            {
                var firstGeneration = generations.EnumerateArray().FirstOrDefault();
                if (firstGeneration.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find generations in Cohere response");
        }
        catch (CohereQuotaExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse Cohere response");
            throw new FormatException("Invalid Cohere response format");
        }
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is CohereRateLimitException || exception is CohereQuotaExceededException)
            return true;

        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("rate") ||
                   message.Contains("free tier");
        }

        return base.ShouldFallback(exception);
    }
}