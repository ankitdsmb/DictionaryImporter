using DictionaryImporter.AI.Core.Exceptions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public class AnthropicProvider : BaseCompletionProvider
    {
        private const string DefaultModel = "claude-3-haiku-20240307";
        private const int FreeTierMaxTokens = 4096;
        private const int FreeTierRequestsPerDay = 100;

        private static long _dailyRequestCount = 0;
        private static DateTime _lastResetDate = DateTime.UtcNow.Date;
        private static readonly object DailyCounterLock = new();

        public override string ProviderName => "Anthropic";
        public override int Priority => 4;
        public override ProviderType Type => ProviderType.TextCompletion;

        public override bool SupportsAudio => false;

        public override bool SupportsVision => true;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public AnthropicProvider(
            HttpClient httpClient,
            ILogger<AnthropicProvider> logger,
            IOptions<ProviderConfiguration> configuration)
            : base(httpClient, logger, configuration)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("Anthropic API key not configured. Provider will be disabled.");
                return;
            }

            ConfigureAuthentication();
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.ImageAnalysis = true;
            Capabilities.MaxTokensLimit = FreeTierMaxTokens;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Clear();

            HttpClient.DefaultRequestHeaders.Add("x-api-key", Configuration.ApiKey);
            HttpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            HttpClient.DefaultRequestHeaders.Add("anthropic-beta", "max-tokens-2024-07-15");

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
                    throw new InvalidOperationException("Anthropic API key not configured");
                }

                if (!CheckDailyLimit())
                {
                    throw new AnthropicQuotaExceededException(
                        $"Anthropic free tier daily limit reached: {FreeTierRequestsPerDay} requests/day");
                }

                ValidateRequest(request);
                IncrementDailyCount();

                var payload = CreateRequestPayload(request);
                var httpRequest = CreateHttpRequest(payload);

                var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
                Logger.LogDebug("Sending request to Anthropic with model {Model}", model);

                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseResponse(content, out var inputTokens, out var outputTokens);

                stopwatch.Stop();

                return new AiResponse
                {
                    Content = result.Trim(),
                    Provider = ProviderName,
                    Model = model,
                    TokensUsed = inputTokens + outputTokens,
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "free_tier", true },
                        { "daily_requests_used", GetDailyRequestCount() },
                        { "daily_requests_remaining", FreeTierRequestsPerDay - GetDailyRequestCount() },
                        { "input_tokens", inputTokens },
                        { "output_tokens", outputTokens },
                        { "supports_vision", true }
                    }
                };
            }
            catch (AnthropicQuotaExceededException ex)
            {
                stopwatch.Stop();
                Logger.LogWarning(ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "Anthropic provider failed");

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
                    "Requested {Requested} tokens exceeds Anthropic free tier limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
            }
        }

        private object CreateRequestPayload(AiRequest request)
        {
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            if (request.ImageData != null || request.ImageUrls?.Count > 0)
            {
                return CreateVisionPayload(request, model);
            }

            return new
            {
                model = model,
                max_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = request.Prompt
                            }
                        }
                    }
                },
                system = request.SystemPrompt ?? "You are a helpful AI assistant."
            };
        }

        private object CreateVisionPayload(AiRequest request, string model)
        {
            var content = new List<object>
            {
                new
                {
                    type = "text",
                    text = request.Prompt
                }
            };

            if (request.ImageData != null)
            {
                var base64Image = Convert.ToBase64String(request.ImageData);
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = GetMimeType(request.ImageFormat),
                        data = base64Image
                    }
                });
            }
            else if (request.ImageUrls?.Count > 0)
            {
                foreach (var url in request.ImageUrls.Take(1))
                {
                    content.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "url",
                            url = url,
                            media_type = "image/jpeg"
                        }
                    });
                }
            }

            return new
            {
                model = model,
                max_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = content
                    }
                },
                system = request.SystemPrompt ?? "You are a helpful AI assistant."
            };
        }

        private string GetMimeType(string imageFormat)
        {
            if (string.IsNullOrEmpty(imageFormat))
                return "image/jpeg";

            return imageFormat.ToLower() switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        private HttpRequestMessage CreateHttpRequest(object payload)
        {
            var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl)
                ? "https://api.anthropic.com/v1/messages"
                : Configuration.BaseUrl;

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

        private string ParseResponse(string jsonResponse, out long inputTokens, out long outputTokens)
        {
            inputTokens = 0;
            outputTokens = 0;

            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);

                if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                    if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                        throw new AnthropicQuotaExceededException($"Anthropic quota exceeded: {errorMessage}");
                    throw new HttpRequestException($"Anthropic API error: {errorMessage}");
                }

                if (jsonDoc.RootElement.TryGetProperty("usage", out var usage))
                {
                    inputTokens = usage.GetProperty("input_tokens").GetInt64();
                    outputTokens = usage.GetProperty("output_tokens").GetInt64();
                }

                if (jsonDoc.RootElement.TryGetProperty("content", out var contentArray))
                {
                    var firstContent = contentArray.EnumerateArray().FirstOrDefault();
                    if (firstContent.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString() ?? string.Empty;
                    }
                }

                throw new FormatException("Could not find content in Anthropic response");
            }
            catch (AnthropicQuotaExceededException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse Anthropic response");
                throw new FormatException("Invalid Anthropic response format");
            }
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is AnthropicQuotaExceededException)
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
            return "https://api.anthropic.com/v1/messages";
        }
    }
}