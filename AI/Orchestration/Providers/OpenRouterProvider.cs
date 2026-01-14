using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public class OpenRouterProvider : BaseCompletionProvider
    {
        private const string DefaultModel = "openai/gpt-3.5-turbo";
        private const int FreeTierMaxTokens = 4000;

        private static int _requestCount = 0;

        private static DateTime _lastResetTime = DateTime.UtcNow;
        private static readonly object RateLimitLock = new();

        public override string ProviderName => "OpenRouter";
        public override int Priority => 1;
        public override ProviderType Type => ProviderType.TextCompletion;

        public override bool SupportsAudio => false;

        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public OpenRouterProvider(
            HttpClient httpClient, ILogger<OpenRouterProvider> logger,
            IOptions<ProviderConfiguration> configuration)
            : base(httpClient, logger, configuration)
        {
            ConfigureAuthentication();
            if (string.IsNullOrEmpty(Configuration.Model))
                Configuration.Model = DefaultModel;
            if (string.IsNullOrEmpty(Configuration.BaseUrl))
                Configuration.BaseUrl = GetDefaultBaseUrl();
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
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("OpenRouter API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }

            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {Configuration.ApiKey}");
            HttpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://dictionary-importer.com");
            HttpClient.DefaultRequestHeaders.Add("X-Title", "Dictionary Importer");
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
        }

        protected virtual string GetDefaultBaseUrl()
        {
            return "https://openrouter.ai/api/v1/chat/completions";
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                if (!Configuration.IsEnabled)
                    throw new InvalidOperationException("OpenRouter provider is disabled");

                if (!CheckRateLimit())
                {
                    throw new HttpRequestException("OpenRouter rate limit exceeded. Please try again in a minute.");
                }

                ValidateRequest(request);

                var payload = CreateRequestPayload(request);
                var httpRequest = CreateHttpRequest(payload);

                Logger.LogDebug("Sending request to OpenRouter with model {Model}", Configuration.Model);

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
                    Model = Configuration.Model,
                    TokensUsed = tokenUsage,
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    Metadata = new Dictionary<string, object>
                    {
                        { "model", Configuration.Model },
                        { "free_tier", true },
                        { "openrouter", true },
                        { "api_version", "v1" },
                        { "rate_limit_remaining", GetRemainingRequests() }
                    }
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "OpenRouter provider failed");

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
                        { "model", Configuration.Model },
                        { "error_type", ex.GetType().Name }
                    }
                };
            }
        }

        private void ValidateRequest(AiRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("Prompt cannot be empty");

            if (request.Prompt.Length > 32000) throw new ArgumentException($"Prompt exceeds OpenRouter limit of 32,000 characters. Length: {request.Prompt.Length}");

            if (request.MaxTokens > FreeTierMaxTokens)
            {
                Logger.LogWarning(
                    "Requested {Requested} tokens exceeds OpenRouter free tier limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
            }
        }

        private bool CheckRateLimit()
        {
            const int requestsPerMinute = 60;

            lock (RateLimitLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastResetTime).TotalMinutes >= 1)
                {
                    _requestCount = 0;
                    _lastResetTime = now;
                }

                if (_requestCount >= requestsPerMinute)
                {
                    var timeToWait = _lastResetTime.AddMinutes(1) - now;
                    Logger.LogWarning("OpenRouter rate limit reached. Try again in {Seconds:F1} seconds", timeToWait.TotalSeconds);
                    return false;
                }

                _requestCount++;
                return true;
            }
        }

        private int GetRemainingRequests()
        {
            const int requestsPerMinute = 60;

            lock (RateLimitLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastResetTime).TotalMinutes >= 1)
                {
                    return requestsPerMinute;
                }

                return Math.Max(0, requestsPerMinute - _requestCount);
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

            var additionalParams = new Dictionary<string, object>();
            if (request.AdditionalParameters != null)
            {
                foreach (var param in request.AdditionalParameters)
                {
                    if (param.Key != "messages" && param.Key != "model")
                    {
                        additionalParams[param.Key] = param.Value;
                    }
                }
            }

            var payload = new Dictionary<string, object>
            {
                { "model", Configuration.Model },
                { "messages", messages },
                { "max_tokens", Math.Min(request.MaxTokens, FreeTierMaxTokens) },
                { "temperature", Math.Clamp(request.Temperature, 0.0, 2.0) },
                { "top_p", 0.9 },
                { "frequency_penalty", 0.0 },
                { "presence_penalty", 0.0 },
                { "stream", false }
            };

            foreach (var param in additionalParams)
            {
                payload[param.Key] = param.Value;
            }

            return payload;
        }

        private HttpRequestMessage CreateHttpRequest(object payload)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            return new HttpRequestMessage(HttpMethod.Post, Configuration.BaseUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, jsonOptions),
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

                    if (errorElement.TryGetProperty("type", out var errorType))
                    {
                        var type = errorType.GetString();
                        if (type == "insufficient_quota" || type == "rate_limit_exceeded")
                        {
                            throw new HttpRequestException($"OpenRouter quota/rate limit error: {errorMessage}");
                        }
                    }

                    throw new HttpRequestException($"OpenRouter API error: {errorMessage}");
                }

                if (jsonDoc.RootElement.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("total_tokens", out var totalTokens))
                    {
                        tokenUsage = totalTokens.GetInt64();
                    }
                    else if (usage.TryGetProperty("completion_tokens", out var completionTokens) &&
                             usage.TryGetProperty("prompt_tokens", out var promptTokens))
                    {
                        tokenUsage = completionTokens.GetInt64() + promptTokens.GetInt64();
                    }
                }

                if (jsonDoc.RootElement.TryGetProperty("choices", out var choices))
                {
                    var choicesArray = choices.EnumerateArray();
                    if (choicesArray.Any())
                    {
                        var firstChoice = choicesArray.First();
                        if (firstChoice.TryGetProperty("message", out var message))
                        {
                            if (message.TryGetProperty("content", out var content))
                            {
                                return content.GetString() ?? string.Empty;
                            }
                        }
                        else if (firstChoice.TryGetProperty("text", out var text))
                        {
                            return text.GetString() ?? string.Empty;
                        }
                    }
                }

                if (jsonDoc.RootElement.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }

                throw new FormatException("Could not find valid response content in OpenRouter response");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse OpenRouter response");
                throw new FormatException("Invalid OpenRouter response format");
            }
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("429") || message.Contains("401") || message.Contains("403") || message.Contains("503") || message.Contains("quota") ||
                       message.Contains("limit") ||
                       message.Contains("rate limit") ||
                       message.Contains("insufficient_quota") ||
                       message.Contains("insufficient credits") ||
                       message.Contains("billing") ||
                       message.Contains("payment required");
            }

            if (exception is TimeoutException || exception is TaskCanceledException)
                return true;

            return base.ShouldFallback(exception);
        }
    }
}