using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class DeepAiProvider : BaseCompletionProvider
{
    private const string DefaultModel = "text-davinci-003-free";
    private const int FreeTierMaxTokens = 300;
    private const int FreeTierRequestsPerDay = 50;

    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object DailyCounterLock = new();

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
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("DeepAI API key not configured. Provider will be disabled.");
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
        HttpClient.DefaultRequestHeaders.Add("api-key", Configuration.ApiKey);
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
                throw new InvalidOperationException("DeepAI API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new DeepAiQuotaExceededException(
                    $"DeepAI free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
            }

            ValidateRequest(request);
            IncrementDailyCount();

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

            return new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = model,
                TokensUsed = CalculateTokenEstimate(request.Prompt, result),
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "free_tier", true },
                        { "daily_requests_used", GetDailyRequestCount() },
                        { "daily_requests_remaining", FreeTierRequestsPerDay - GetDailyRequestCount() },
                        { "max_tokens_limit", FreeTierMaxTokens }
                    }
            };
        }
        catch (DeepAiQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "DeepAI provider failed");
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

        if (request.Prompt.Length > 4000)
            throw new ArgumentException($"Prompt exceeds DeepAI limit of 4000 characters. Length: {request.Prompt.Length}");

        if (request.MaxTokens > FreeTierMaxTokens)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds DeepAI free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        return new
        {
            text = request.Prompt,
            model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            temperature = Math.Clamp(request.Temperature, 0.1, 1.0),
            max_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens)
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.deepai.org/api/text-generator" : Configuration.BaseUrl;

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

    private string ParseResponse(string jsonResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            if (jsonDoc.RootElement.TryGetProperty("err", out var errElement))
            {
                var errorMessage = errElement.GetString() ?? "Unknown error";
                throw new DeepAiQuotaExceededException($"DeepAI error: {errorMessage}");
            }

            if (jsonDoc.RootElement.TryGetProperty("output", out var outputElement))
                return outputElement.GetString() ?? string.Empty;

            if (jsonDoc.RootElement.TryGetProperty("text", out var textElement))
                return textElement.GetString() ?? string.Empty;

            if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.TryGetProperty("output", out var nestedOutput))
                    return nestedOutput.GetString() ?? string.Empty;
            }

            foreach (var property in jsonDoc.RootElement.EnumerateObject())
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
        catch (DeepAiQuotaExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse DeepAI response");
            throw new FormatException("Invalid DeepAI response format");
        }
    }

    private long CalculateTokenEstimate(string prompt, string response)
    {
        var promptTokens = prompt.Length / 4;
        var responseTokens = response.Length / 4;
        return promptTokens + responseTokens;
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is DeepAiQuotaExceededException)
            return true;

        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("free tier") ||
                   message.Contains("insufficient credits");
        }

        return base.ShouldFallback(exception);
    }
}