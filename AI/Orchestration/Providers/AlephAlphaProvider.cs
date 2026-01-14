using DictionaryImporter.AI.Core.Exceptions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public class AlephAlphaProvider : BaseCompletionProvider
    {
        private const string DefaultModel = "luminous-base";
        private const int FreeTierMaxTokens = 2048;
        private const int FreeTierRequestsPerMonth = 1000;

        private static long _monthlyRequestCount = 0;
        private static DateTime _lastResetMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        private static readonly object MonthlyCounterLock = new();

        public override string ProviderName => "AlephAlpha";
        public override int Priority => 11;
        public override ProviderType Type => ProviderType.TextCompletion;

        public override bool SupportsAudio => false;

        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => false;
        public override bool IsLocal => false;

        public AlephAlphaProvider(
            HttpClient httpClient,
            ILogger<AlephAlphaProvider> logger,
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

                if (!CheckMonthlyLimit())
                {
                    throw new AlephAlphaQuotaExceededException(
                        $"Aleph Alpha free tier monthly limit reached: {FreeTierRequestsPerMonth} requests/month");
                }

                ValidateRequest(request);
                IncrementMonthlyCount();

                var payload = CreateRequestPayload(request);
                var httpRequest = CreateHttpRequest(payload);

                var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
                Logger.LogDebug("Sending request to Aleph Alpha with model {Model}", model);

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
                        { "monthly_requests_used", GetMonthlyRequestCount() },
                        { "monthly_requests_remaining", FreeTierRequestsPerMonth - GetMonthlyRequestCount() },
                        { "european_data_center", true }
                    }
                };
            }
            catch (AlephAlphaQuotaExceededException ex)
            {
                stopwatch.Stop();
                Logger.LogWarning(ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "Aleph Alpha provider failed");

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

        private bool CheckMonthlyLimit()
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth > _lastResetMonth)
                {
                    _monthlyRequestCount = 0;
                    _lastResetMonth = currentMonth;
                }
                return _monthlyRequestCount < FreeTierRequestsPerMonth;
            }
        }

        private void IncrementMonthlyCount()
        {
            lock (MonthlyCounterLock)
            {
                _monthlyRequestCount++;
            }
        }

        private long GetMonthlyRequestCount()
        {
            lock (MonthlyCounterLock)
            {
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                if (currentMonth > _lastResetMonth)
                {
                    _monthlyRequestCount = 0;
                    _lastResetMonth = currentMonth;
                }
                return _monthlyRequestCount;
            }
        }

        private void ValidateRequest(AiRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("Prompt cannot be empty");

            if (request.MaxTokens > FreeTierMaxTokens)
            {
                Logger.LogWarning(
                    "Requested {Requested} tokens exceeds Aleph Alpha free tier limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, FreeTierMaxTokens, FreeTierMaxTokens);
            }
        }

        private object CreateRequestPayload(AiRequest request)
        {
            return new
            {
                model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                prompt = request.Prompt,
                maximum_tokens = Math.Min(request.MaxTokens, FreeTierMaxTokens),
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                top_k = 0,
                top_p = 0.9,
                presence_penalty = 0.0,
                frequency_penalty = 0.0,
                repetition_penalties_include_prompt = false,
                best_of = 1,
                stop_sequences = Array.Empty<string>()
            };
        }

        private HttpRequestMessage CreateHttpRequest(object payload)
        {
            var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl)
                ? "https://api.aleph-alpha.com/complete"
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
                        throw new AlephAlphaQuotaExceededException($"Aleph Alpha quota exceeded: {errorMessage}");
                    throw new HttpRequestException($"Aleph Alpha API error: {errorMessage}");
                }

                if (jsonDoc.RootElement.TryGetProperty("completions", out var completions))
                {
                    var firstCompletion = completions.EnumerateArray().FirstOrDefault();
                    if (firstCompletion.TryGetProperty("completion", out var completion))
                    {
                        var text = completion.GetString() ?? string.Empty;

                        tokenUsage = EstimateTokenUsage(text);
                        return text;
                    }
                }

                throw new FormatException("Could not find completions in Aleph Alpha response");
            }
            catch (AlephAlphaQuotaExceededException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse Aleph Alpha response");
                throw new FormatException("Invalid Aleph Alpha response format");
            }
        }

        private long EstimateTokenUsage(string prompt, string result)
        {
            return (prompt.Length + result.Length) / 4;
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is AlephAlphaQuotaExceededException)
                return true;

            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("429") ||
                       message.Contains("quota") ||
                       message.Contains("limit") ||
                       message.Contains("monthly") ||
                       message.Contains("free tier");
            }

            return base.ShouldFallback(exception);
        }

        protected virtual string GetDefaultBaseUrl()
        {
            return "https://api.aleph-alpha.com/complete";
        }
    }
}