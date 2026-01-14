using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class ReplicateProvider : BaseCompletionProvider
{
    private const string DefaultModel = "meta/llama-2-70b-chat";
    private const int FreeTierMaxTokens = 500;
    private const int FreeTierSecondsPerMonth = 1000;
    private const int FreeTierRequestsPerDay = 50;

    private static long _monthlySecondsUsed = 0;
    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object MonthlyCounterLock = new();
    private static readonly object DailyCounterLock = new();

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
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("Replicate API key not configured. Provider will be disabled.");
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
                throw new InvalidOperationException("Replicate API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new ReplicateQuotaExceededException(
                    $"Replicate free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
            }

            var estimatedSeconds = EstimateProcessingTime(request);
            if (!CheckMonthlyLimit(estimatedSeconds))
            {
                throw new ReplicateQuotaExceededException(
                    $"Replicate free tier monthly limit reached: {FreeTierSecondsPerMonth} seconds/month");
            }

            ValidateRequest(request);
            IncrementDailyCount();

            var predictionId = await CreatePredictionAsync(request, cancellationToken);
            var result = await PollPredictionAsync(predictionId, cancellationToken);

            stopwatch.Stop();

            var actualSeconds = (long)stopwatch.Elapsed.TotalSeconds;
            IncrementMonthlyUsage(actualSeconds);

            return new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                TokensUsed = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                        { "free_tier", true },
                        { "daily_requests_used", GetDailyRequestCount() },
                        { "daily_requests_remaining", FreeTierRequestsPerDay - GetDailyRequestCount() },
                        { "monthly_seconds_used", GetMonthlySecondsUsed() },
                        { "monthly_seconds_remaining", FreeTierSecondsPerMonth - GetMonthlySecondsUsed() },
                        { "prediction_id", predictionId },
                        { "open_source", true }
                    }
            };
        }
        catch (ReplicateQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Replicate provider failed");
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

    private long EstimateProcessingTime(AiRequest request)
    {
        return (long)(request.MaxTokens * 0.1);
    }

    private bool CheckMonthlyLimit(long additionalSeconds)
    {
        lock (MonthlyCounterLock)
        {
            var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (currentMonth > _lastResetMonth)
            {
                _monthlySecondsUsed = 0;
                _lastResetMonth = currentMonth;
            }
            return (_monthlySecondsUsed + additionalSeconds) <= FreeTierSecondsPerMonth;
        }
    }

    private void IncrementMonthlyUsage(long seconds)
    {
        lock (MonthlyCounterLock)
        {
            _monthlySecondsUsed += seconds;
        }
    }

    private long GetMonthlySecondsUsed()
    {
        lock (MonthlyCounterLock)
        {
            var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (currentMonth > _lastResetMonth)
            {
                _monthlySecondsUsed = 0;
                _lastResetMonth = currentMonth;
            }
            return _monthlySecondsUsed;
        }
    }

    private void ValidateRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt cannot be empty");

        if (request.MaxTokens > FreeTierMaxTokens)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds Replicate free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
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
                max_length = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                top_p = 0.9,
                repetition_penalty = 1.0
            }
        };

        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.replicate.com/v1/predictions" : Configuration.BaseUrl;

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl)
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
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.replicate.com/v1/predictions" : Configuration.BaseUrl;

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

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is ReplicateQuotaExceededException)
            return true;

        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("monthly") ||
                   message.Contains("free tier") ||
                   message.Contains("credit");
        }

        return base.ShouldFallback(exception);
    }
}