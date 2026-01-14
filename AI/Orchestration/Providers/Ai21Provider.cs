using DictionaryImporter.AI.Core.Exceptions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public class Ai21Provider : BaseCompletionProvider
    {
        private const string DefaultModel = "j2-light";
        private const int FreeTierMaxTokens = 512;
        private const int FreeTierDailyRequests = 100;

        private static long _dailyRequestCount = 0;
        private static DateTime _lastResetDate = DateTime.UtcNow.Date;
        private static readonly object DailyCounterLock = new();

        public override string ProviderName => "AI21";
        public override int Priority => 7;
        public override ProviderType Type => ProviderType.TextCompletion;

        public override bool SupportsAudio => false;

        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public Ai21Provider(
            HttpClient httpClient,
            ILogger<Ai21Provider> logger,
            IOptions<ProviderConfiguration> configuration)
            : base(httpClient, logger, configuration)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("AI21 API key not configured. Provider will be disabled.");
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
                    throw new InvalidOperationException("AI21 API key not configured");
                }

                if (!CheckDailyLimit())
                {
                    throw new Ai21QuotaExceededException(
                        $"AI21 free tier daily limit reached: {FreeTierDailyRequests} requests/day");
                }

                ValidateRequest(request);
                IncrementDailyCount();

                var payload = CreateRequestPayload(request);
                var httpRequest = CreateHttpRequest(payload);

                var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
                Logger.LogDebug("Sending request to AI21 with model {Model}", model);

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
                        { "daily_requests_remaining", FreeTierDailyRequests - GetDailyRequestCount() }
                    }
                };
            }
            catch (Ai21QuotaExceededException ex)
            {
                stopwatch.Stop();
                Logger.LogWarning(ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "AI21 provider failed");

                if (ShouldFallback(ex))
                    throw;

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

            if (request.Prompt.Length > 10000)
                throw new ArgumentException($"Prompt exceeds AI21 limit of 10,000 characters");

            if (request.MaxTokens > FreeTierMaxTokens)
            {
                Logger.LogWarning(
                    "Requested {Requested} tokens exceeds AI21 free tier limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
            }
        }

        private object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                prompt = request.Prompt,
                numResults = 1,
                maxTokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                topKReturn = 0,
                topP = 0.95,
                stopSequences = Array.Empty<string>(),
                countPenalty = new
                {
                    scale = 0,
                    applyToNumbers = false,
                    applyToPunctuation = false,
                    applyToStopwords = false,
                    applyToWhitespaces = false,
                    applyToEmojis = false
                },
                frequencyPenalty = new
                {
                    scale = 0,
                    applyToNumbers = false,
                    applyToPunctuation = false,
                    applyToStopwords = false,
                    applyToWhitespaces = false,
                    applyToEmojis = false
                },
                presencePenalty = new
                {
                    scale = 0,
                    applyToNumbers = false,
                    applyToPunctuation = false,
                    applyToStopwords = false,
                    applyToWhitespaces = false,
                    applyToEmojis = false
                }
            };
        }

        private HttpRequestMessage CreateHttpRequest(object payload)
        {
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
            var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl)
                ? $"https://api.ai21.com/studio/v1/{model}/complete"
                : Configuration.BaseUrl;

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

                if (jsonDoc.RootElement.TryGetProperty("detail", out var detailElement))
                {
                    var errorMessage = detailElement.GetString() ?? "Unknown error";
                    if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                        throw new Ai21QuotaExceededException($"AI21 quota exceeded: {errorMessage}");
                    throw new HttpRequestException($"AI21 API error: {errorMessage}");
                }

                if (jsonDoc.RootElement.TryGetProperty("completions", out var completions))
                {
                    var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                    if (firstCompletion.TryGetProperty("data", out var data))
                    {
                        var text = data.GetProperty("text").GetString() ?? string.Empty;

                        if (jsonDoc.RootElement.TryGetProperty("prompt", out var prompt) &&
                            prompt.TryGetProperty("tokens", out var promptTokens))
                        {
                            tokenUsage += promptTokens.EnumerateArray().Count();
                        }
                        tokenUsage += text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                        return text;
                    }
                }

                throw new FormatException("Could not find completions in AI21 response");
            }
            catch (Ai21QuotaExceededException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse AI21 response");
                throw new FormatException("Invalid AI21 response format");
            }
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is Ai21QuotaExceededException)
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

        protected virtual string GetDefaultBaseUrl()
        {
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
            return $"https://api.ai21.com/studio/v1/{model}/complete";
        }
    }
}