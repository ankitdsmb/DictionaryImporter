using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;
using System.Globalization;

public class NlpCloudProvider : BaseCompletionProvider
{
    private const string DefaultModel = "finetuned-llama-2-70b";
    private const int FreeTierMaxTokens = 1024;
    private const int FreeTierRequestsPerMinute = 3;
    private const int FreeTierRequestsPerDay = 100;

    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object DailyCounterLock = new();

    private readonly ConcurrentDictionary<string, DateTime> _rateLimitTracker = new();
    private readonly object _rateLimitLock = new();

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
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("NLP Cloud API key not configured. Provider will be disabled.");
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
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Token {Configuration.ApiKey}");
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
                throw new InvalidOperationException("NLP Cloud API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new NlpCloudQuotaExceededException(
                    $"NLP Cloud free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
            }

            CheckRateLimit();

            ValidateRequest(request);
            IncrementDailyCount();

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
            UpdateRateLimit();

            return new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = model,
                TokensUsed = EstimateTokenUsage(request.Prompt, result),
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "free_tier", true },
                        { "daily_requests_used", GetDailyRequestCount() },
                        { "daily_requests_remaining", FreeTierRequestsPerDay - GetDailyRequestCount() },
                        { "rate_limit_remaining", GetRemainingRequests() },
                        { "nlp_service", true }
                    }
            };
        }
        catch (NlpCloudRateLimitException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (NlpCloudQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "NLP Cloud provider failed");
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
                throw new NlpCloudRateLimitException(
                    $"NLP Cloud free tier rate limit exceeded. {FreeTierRequestsPerMinute} requests/minute allowed. " +
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
                "Requested {Requested} tokens exceeds NLP Cloud free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        return new
        {
            text = request.Prompt,
            model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            max_length = Math.Min(request.MaxTokens, FreeTierMaxTokens),
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
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.nlpcloud.io/v1/gpu/finetuned-llama-2-70b/generation" : Configuration.BaseUrl;

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

    private string ParseResponse(string jsonResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            if (jsonDoc.RootElement.TryGetProperty("detail", out var detailElement))
            {
                var errorMessage = detailElement.GetString() ?? "Unknown error";
                if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                    throw new NlpCloudQuotaExceededException($"NLP Cloud quota exceeded: {errorMessage}");
                throw new HttpRequestException($"NLP Cloud API error: {errorMessage}");
            }

            if (jsonDoc.RootElement.TryGetProperty("generated_text", out var generatedText))
            {
                return generatedText.GetString() ?? string.Empty;
            }

            throw new FormatException("Could not find generated_text in NLP Cloud response");
        }
        catch (NlpCloudQuotaExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse NLP Cloud response");
            throw new FormatException("Invalid NLP Cloud response format");
        }
    }

    private long EstimateTokenUsage(string prompt, string response)
    {
        return (prompt.Length + response.Length) / 4;
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is NlpCloudRateLimitException || exception is NlpCloudQuotaExceededException)
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