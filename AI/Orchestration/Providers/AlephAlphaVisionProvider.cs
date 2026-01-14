using DictionaryImporter.AI.Core.Exceptions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public class AlephAlphaVisionProvider : BaseCompletionProvider
    {
        private const string DefaultModel = "luminous-base";
        private const int FreeTierMaxTokens = 2048;
        private const int FreeTierImagesPerMonth = 100;

        private static long _monthlyImageCount = 0;
        private static DateTime _lastResetMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        private static readonly object MonthlyCounterLock = new();

        public override string ProviderName => "AlephAlphaVision";
        public override int Priority => 18;
        public override ProviderType Type => ProviderType.VisionAnalysis;

        public override bool SupportsAudio => false;

        public override bool SupportsVision => true;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public AlephAlphaVisionProvider(
            HttpClient httpClient,
            ILogger<AlephAlphaVisionProvider> logger,
            IOptions<ProviderConfiguration> configuration)
            : base(httpClient, logger, configuration)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("Aleph Alpha API key not configured. Provider will be disabled.");
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
                    throw new InvalidOperationException("Aleph Alpha API key not configured");
                }

                if (!IsImageRequest(request))
                    return await HandleTextCompletionAsync(request, cancellationToken);

                if (!CheckMonthlyImageLimit())
                {
                    throw new AlephAlphaVisionQuotaExceededException(
                        $"Aleph Alpha Vision free tier monthly limit reached: {FreeTierImagesPerMonth} images/month");
                }

                ValidateImageRequest(request);
                IncrementMonthlyImageCount();

                var payload = CreateMultimodalPayload(request);
                var httpRequest = CreateHttpRequest(payload);

                Logger.LogDebug("Sending multimodal request to Aleph Alpha Vision");

                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseVisionResponse(content);

                stopwatch.Stop();

                return new AiResponse
                {
                    Content = result.Trim(),
                    Provider = ProviderName,
                    TokensUsed = EstimateVisionTokenUsage(request, result),
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    Metadata = new Dictionary<string, object>
                    {
                        { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                        { "free_tier", true },
                        { "monthly_images_analyzed", GetMonthlyImageCount() },
                        { "monthly_images_remaining", FreeTierImagesPerMonth - GetMonthlyImageCount() },
                        { "multimodal", true },
                        { "vision_capabilities", true },
                        { "european_data_center", true }
                    }
                };
            }
            catch (AlephAlphaVisionQuotaExceededException ex)
            {
                stopwatch.Stop();
                Logger.LogWarning(ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "Aleph Alpha Vision provider failed");

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

        private bool IsImageRequest(AiRequest request)
        {
            return request.AdditionalParameters?.ContainsKey("image_url") == true ||
                   request.AdditionalParameters?.ContainsKey("image_base64") == true ||
                   (request.Prompt?.Contains("data:image/") == true && request.Prompt.Contains("base64"));
        }

        private async Task<AiResponse> HandleTextCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken)
        {
            var payload = new
            {
                model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                prompt = request.Prompt,
                maximum_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0)
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetDefaultBaseUrl())
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = ParseTextResponse(content);

            return new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                TokensUsed = EstimateTextTokenUsage(request.Prompt, result),
                ProcessingTime = TimeSpan.Zero,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                {
                    { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                    { "text_only", true }
                }
            };
        }

        private bool CheckMonthlyImageLimit()
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth > _lastResetMonth)
                {
                    _monthlyImageCount = 0;
                    _lastResetMonth = currentMonth;
                }
                return _monthlyImageCount < FreeTierImagesPerMonth;
            }
        }

        private void IncrementMonthlyImageCount()
        {
            lock (MonthlyCounterLock)
            {
                _monthlyImageCount++;
            }
        }

        private long GetMonthlyImageCount()
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth > _lastResetMonth)
                {
                    _monthlyImageCount = 0;
                    _lastResetMonth = currentMonth;
                }
                return _monthlyImageCount;
            }
        }

        private void ValidateImageRequest(AiRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt) &&
                !(request.AdditionalParameters?.ContainsKey("image_url") == true ||
                  request.AdditionalParameters?.ContainsKey("image_base64") == true))
            {
                throw new ArgumentException("Either prompt or image data required for vision analysis");
            }
        }

        private object CreateMultimodalPayload(AiRequest request)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                ["prompt"] = request.Prompt ?? "Describe this image",
                ["maximum_tokens"] = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                ["temperature"] = Math.Clamp(request.Temperature, 0.0, 1.0)
            };

            if (request.AdditionalParameters != null)
            {
                if (request.AdditionalParameters.TryGetValue("image_url", out var imageUrl))
                {
                    payload["image_url"] = imageUrl;
                }
                else if (request.AdditionalParameters.TryGetValue("image_base64", out var imageBase64))
                {
                    payload["image_base64"] = imageBase64;
                }
            }

            return payload;
        }

        private HttpRequestMessage CreateHttpRequest(object payload)
        {
            var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl)
                ? GetDefaultBaseUrl()
                : Configuration.BaseUrl;

            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Content-Type", "application/json");
            return request;
        }

        private string ParseVisionResponse(string jsonResponse)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);

                if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var errorMessage = errorElement.GetProperty("message").GetString() ?? "Unknown error";
                    if (errorMessage.Contains("quota") || errorMessage.Contains("limit"))
                        throw new AlephAlphaVisionQuotaExceededException($"Aleph Alpha Vision quota exceeded: {errorMessage}");
                    throw new HttpRequestException($"Aleph Alpha Vision API error: {errorMessage}");
                }

                if (jsonDoc.RootElement.TryGetProperty("completions", out var completions))
                {
                    var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                    if (firstCompletion.TryGetProperty("completion", out var completion))
                    {
                        return completion.GetString() ?? string.Empty;
                    }
                }

                throw new FormatException("Could not find completions in Aleph Alpha Vision response");
            }
            catch (AlephAlphaVisionQuotaExceededException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse Aleph Alpha Vision response");
                throw new FormatException("Invalid Aleph Alpha Vision response format");
            }
        }

        private string ParseTextResponse(string jsonResponse)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);

                if (jsonDoc.RootElement.TryGetProperty("completions", out var completions))
                {
                    var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                    if (firstCompletion.TryGetProperty("completion", out var completion))
                    {
                        return completion.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse Aleph Alpha text response");
                return string.Empty;
            }
        }

        private long EstimateVisionTokenUsage(AiRequest request, string result)
        {
            var baseTokens = ((request.Prompt?.Length ?? 0) + result.Length) / 4;
            return baseTokens + 1000;
        }

        private long EstimateTextTokenUsage(string prompt, string result)
        {
            return (prompt.Length + result.Length) / 4;
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is AlephAlphaVisionQuotaExceededException)
                return true;

            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("429") ||
                       message.Contains("quota") ||
                       message.Contains("limit") ||
                       message.Contains("monthly") ||
                       message.Contains("free tier") ||
                       message.Contains("vision");
            }

            return base.ShouldFallback(exception);
        }

        protected virtual string GetDefaultBaseUrl()
        {
            return "https://api.aleph-alpha.com/complete";
        }
    }
}