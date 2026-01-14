using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class HuggingFaceProvider : BaseCompletionProvider
{
    private const string DefaultModel = "gpt2";
    private const int FreeTierMaxTokens = 250;
    private const int FreeTierRequestsPerDay = 100;

    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object DailyCounterLock = new();

    public override string ProviderName => "HuggingFace";
    public override int Priority => 2;
    public override ProviderType Type => ProviderType.TextCompletion;

    public override bool SupportsAudio => false;

    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public HuggingFaceProvider(
        HttpClient httpClient,
        ILogger<HuggingFaceProvider> logger,
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("Hugging Face API key not configured. Provider will be disabled.");
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
                throw new InvalidOperationException("Hugging Face API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new HttpRequestException(
                    $"Hugging Face free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
            }

            ValidateRequest(request);
            IncrementDailyCount();

            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to Hugging Face with model {Model}", model);

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
                TokensUsed = EstimateTokenUsage(request.Prompt, result),
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "free_tier", true },
                        { "daily_requests_used", GetDailyRequestCount() },
                        { "daily_requests_remaining", FreeTierRequestsPerDay - GetDailyRequestCount() },
                        { "huggingface", true }
                    }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Hugging Face provider failed");
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
                "Requested {Requested} tokens exceeds Hugging Face free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        return new
        {
            inputs = request.Prompt,
            parameters = new
            {
                max_new_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                temperature = Math.Clamp(request.Temperature, 0.1, 2.0),
                top_p = 0.95,
                top_k = 50,
                repetition_penalty = 1.0,
                do_sample = true,
                return_full_text = false,
                num_return_sequences = 1
            }
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            $"https://api-inference.huggingface.co/models/{model}" : Configuration.BaseUrl;

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

            if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetString() ?? "Unknown error";
                if (errorMessage.Contains("loading") || errorMessage.Contains("model is currently loading"))
                {
                    throw new HttpRequestException($"Hugging Face model loading: {errorMessage}");
                }
                throw new HttpRequestException($"Hugging Face API error: {errorMessage}");
            }

            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var firstElement = jsonDoc.RootElement.EnumerateArray().FirstOrDefault();
                if (firstElement.TryGetProperty("generated_text", out var generatedText))
                {
                    return generatedText.GetString() ?? string.Empty;
                }
            }
            else if (jsonDoc.RootElement.TryGetProperty("generated_text", out var generatedText))
            {
                return generatedText.GetString() ?? string.Empty;
            }

            throw new FormatException("Invalid Hugging Face response format");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse Hugging Face response");
            throw new FormatException("Invalid Hugging Face response format");
        }
    }

    private long EstimateTokenUsage(string prompt, string result)
    {
        return (prompt.Length + result.Length) / 4;
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("rate limit") ||
                   message.Contains("model is currently loading");
        }

        return base.ShouldFallback(exception);
    }
}