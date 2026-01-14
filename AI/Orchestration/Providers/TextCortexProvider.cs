using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class TextCortexProvider : BaseCompletionProvider
{
    private const string DefaultModel = "gpt-4";
    private const int FreeTierMaxTokens = 2000;
    private const int FreeTierDailyRequests = 50;

    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object DailyCounterLock = new();

    public override string ProviderName => "TextCortex";
    public override int Priority => 17;
    public override ProviderType Type => ProviderType.TextCompletion;

    public override bool SupportsAudio => false;

    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public TextCortexProvider(
        HttpClient httpClient,
        ILogger<TextCortexProvider> logger,
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("TextCortex API key not configured. Provider will be disabled.");
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
                throw new InvalidOperationException("TextCortex API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new TextCortexQuotaExceededException(
                    $"TextCortex free tier daily limit reached: {FreeTierDailyRequests} requests/day");
            }

            ValidateRequest(request);
            IncrementDailyCount();

            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to TextCortex with model {Model}", model);

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
                        { "daily_requests_remaining", FreeTierDailyRequests - GetDailyRequestCount() },
                        { "creativity_level", request.Temperature > 0.7 ? "high" : request.Temperature > 0.3 ? "medium" : "low" }
                    }
            };
        }
        catch (TextCortexQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "TextCortex provider failed");
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
            return _dailyRequestCount < FreeTierDailyRequests;
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
                "Requested {Requested} tokens exceeds TextCortex free tier limit of {Limit}. Using {Limit} instead.",
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
            temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
            top_p = 0.9,
            frequency_penalty = 0.0,
            presence_penalty = 0.0,
            n = 1,
            stream = false
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.textcortex.com/v1/texts/completions" : Configuration.BaseUrl;

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
                    throw new TextCortexQuotaExceededException($"TextCortex quota exceeded: {errorMessage}");
                throw new HttpRequestException($"TextCortex API error: {errorMessage}");
            }

            if (jsonDoc.RootElement.TryGetProperty("data", out var data))
            {
                var outputs = data.GetProperty("outputs");
                var firstOutput = outputs.EnumerateArray().FirstOrDefault();
                if (firstOutput.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString() ?? string.Empty;
                    tokenUsage = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 2;
                    return text;
                }
            }

            throw new FormatException("Could not find data.outputs in TextCortex response");
        }
        catch (TextCortexQuotaExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse TextCortex response");
            throw new FormatException("Invalid TextCortex response format");
        }
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is TextCortexQuotaExceededException)
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