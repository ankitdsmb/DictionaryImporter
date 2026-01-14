using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class WatsonProvider : BaseCompletionProvider
{
    private const string DefaultModel = "ibm/granite-13b-chat-v2";
    private const string DefaultRegion = "us-south";
    private const int FreeTierMaxTokens = 1000;
    private const int FreeTierRequestsPerMinute = 100;
    private const int FreeTierRequestsPerDay = 1000;

    private readonly string _iamApiKey;
    private string? _accessToken;
    private DateTime _tokenExpiry;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static long _dailyRequestCount = 0;
    private static DateTime _lastResetDate = DateTime.UtcNow.Date;
    private static readonly object DailyCounterLock = new();

    public override string ProviderName => "Watson";
    public override int Priority => 5;
    public override ProviderType Type => ProviderType.TextCompletion;

    public override bool SupportsAudio => false;

    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public WatsonProvider(
        HttpClient httpClient,
        ILogger<WatsonProvider> logger,
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        _iamApiKey = Configuration.ApiKey;

        if (string.IsNullOrEmpty(_iamApiKey))
        {
            Logger.LogWarning("Watson API key not configured. Provider will be disabled.");
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
            if (string.IsNullOrEmpty(_iamApiKey))
            {
                throw new InvalidOperationException("Watson API key not configured");
            }

            if (!CheckDailyLimit())
            {
                throw new WatsonQuotaExceededException(
                    $"Watson free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
            }

            ValidateRequest(request);
            IncrementDailyCount();

            var token = await GetOrRefreshTokenAsync(cancellationToken);
            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(token, payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to Watson with model {Model}", model);

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
                TokensUsed = CalculateTokenUsage(request.Prompt, result),
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "free_tier", true },
                        { "daily_requests_used", GetDailyRequestCount() },
                        { "daily_requests_remaining", FreeTierRequestsPerDay - GetDailyRequestCount() },
                        { "region", DefaultRegion }
                    }
            };
        }
        catch (WatsonQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (WatsonAuthorizationException ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Watson authorization failed");
            await ClearTokenAsync();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Watson provider failed");
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

        if (request.Prompt.Length > 20000)
            throw new ArgumentException($"Prompt exceeds Watson limit of 20,000 characters. Length: {request.Prompt.Length}");

        if (request.MaxTokens > FreeTierMaxTokens)
        {
            Logger.LogWarning(
                "Requested {Requested} tokens exceeds Watson free tier limit of {Limit}. Using {Limit} instead.",
                request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
        }
    }

    private async Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiry > DateTime.UtcNow.AddMinutes(5))
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiry > DateTime.UtcNow.AddMinutes(5))
                return _accessToken;

            Logger.LogDebug("Refreshing IBM IAM token");

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://iam.cloud.ibm.com/identity/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                        new KeyValuePair<string, string>("grant_type", "urn:ibm:params:oauth:grant-type:apikey"),
                        new KeyValuePair<string, string>("apikey", _iamApiKey)
                    })
            };

            tokenRequest.Headers.Add("Accept", "application/json");
            tokenRequest.Headers.Add("Authorization", "Basic Yng6Yng=");

            var response = await HttpClient.SendAsync(tokenRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("IBM IAM token request failed: {StatusCode} - {Content}", response.StatusCode, content);
                throw new WatsonAuthorizationException($"Failed to obtain IAM token: {response.StatusCode}");
            }

            using var jsonDoc = JsonDocument.Parse(content);
            _accessToken = jsonDoc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = jsonDoc.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

            Logger.LogDebug("IBM IAM token obtained, expires in {ExpiresIn} seconds", expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private object CreateRequestPayload(AiRequest request)
    {
        return new
        {
            model_id = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            input = request.Prompt,
            parameters = new
            {
                decoding_method = "greedy",
                max_new_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                min_new_tokens = 1,
                repetition_penalty = 1.0,
                temperature = Math.Clamp(request.Temperature, 0.1, 2.0),
                top_k = 50,
                top_p = 1.0,
                random_seed = DateTime.UtcNow.Millisecond
            },
            project_id = Environment.GetEnvironmentVariable("WATSON_PROJECT_ID") ?? ""
        };
    }

    private HttpRequestMessage CreateHttpRequest(string token, object payload)
    {
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            $"https://{DefaultRegion}.ml.cloud.ibm.com/ml/v1/text/generation?version=2023-05-29" : Configuration.BaseUrl;

        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
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

        request.Headers.Add("Authorization", $"Bearer {token}");

        var projectId = Environment.GetEnvironmentVariable("WATSON_PROJECT_ID");
        if (!string.IsNullOrEmpty(projectId))
            request.Headers.Add("ML-Instance-ID", projectId);

        return request;
    }

    private string ParseResponse(string jsonResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            if (jsonDoc.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                var error = errorsElement.EnumerateArray().FirstOrDefault();
                var errorMessage = error.GetProperty("message").GetString() ?? "Unknown error";

                if (errorMessage.Contains("quota") || errorMessage.Contains("limit") || errorMessage.Contains("exceeded"))
                    throw new WatsonQuotaExceededException($"Watson quota exceeded: {errorMessage}");

                throw new HttpRequestException($"Watson API error: {errorMessage}");
            }

            if (jsonDoc.RootElement.TryGetProperty("results", out var resultsElement))
            {
                var firstResult = resultsElement.EnumerateArray().FirstOrDefault();
                if (firstResult.TryGetProperty("generated_text", out var generatedTextElement))
                {
                    return generatedTextElement.GetString() ?? string.Empty;
                }
            }

            if (jsonDoc.RootElement.TryGetProperty("generated_text", out var generatedText))
            {
                return generatedText.GetString() ?? string.Empty;
            }

            throw new FormatException("Could not find generated_text in Watson response");
        }
        catch (WatsonQuotaExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse Watson response");
            throw new FormatException("Invalid Watson response format");
        }
    }

    private static long CalculateTokenUsage(string prompt, string response)
    {
        return (prompt.Length + response.Length) / 4;
    }

    private async Task ClearTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            _accessToken = null;
            _tokenExpiry = DateTime.MinValue;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is WatsonQuotaExceededException) return true;
        if (exception is WatsonAuthorizationException)
            return true;

        if (exception is not HttpRequestException httpEx) return base.ShouldFallback(exception);
        var message = httpEx.Message.ToLowerInvariant();
        return message.Contains("429") ||
               message.Contains("quota") ||
               message.Contains("limit") ||
               message.Contains("exceeded") ||
               message.Contains("insufficient capacity") ||
               message.Contains("plan limits");
    }
}