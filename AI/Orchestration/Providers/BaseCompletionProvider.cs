using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Core.Exceptions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    public abstract class BaseCompletionProvider : ICompletionProvider
    {
        protected readonly HttpClient HttpClient;
        protected readonly ILogger Logger;
        protected readonly ProviderConfiguration Configuration;
        protected readonly ProviderCapabilities Capabilities;
        protected readonly AsyncRetryPolicy<HttpResponseMessage> RetryPolicy;
        protected readonly AsyncCircuitBreakerPolicy<HttpResponseMessage> CircuitBreakerPolicy;
        protected readonly AsyncTimeoutPolicy TimeoutPolicy;
        protected readonly JsonSerializerOptions JsonSerializerOptions;
        protected readonly ConcurrentDictionary<string, ProviderPerformance> PerformanceMetrics;

        public abstract string ProviderName { get; }
        public abstract int Priority { get; }
        public abstract ProviderType Type { get; }
        public virtual bool IsEnabled => Configuration?.IsEnabled ?? false;

        public virtual bool SupportsAudio => false;

        public virtual bool SupportsVision => false;
        public virtual bool SupportsImages => false;
        public virtual bool SupportsTextToSpeech => false;
        public virtual bool SupportsTranscription => false;
        public virtual bool IsLocal => false;

        ProviderCapabilities ICompletionProvider.Capabilities => Capabilities;

        protected BaseCompletionProvider(
            HttpClient httpClient,
            ILogger logger,
            IOptions<ProviderConfiguration> configuration)
        {
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

            PerformanceMetrics = new ConcurrentDictionary<string, ProviderPerformance>();
            Capabilities = new ProviderCapabilities();
            JsonSerializerOptions = CreateJsonSerializerOptions();

            RetryPolicy = CreateRetryPolicy();
            CircuitBreakerPolicy = CreateCircuitBreakerPolicy();
            TimeoutPolicy = CreateTimeoutPolicy();

            ConfigureHttpClient();
            ConfigureCapabilities();
        }

        private AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy()
        {
            var maxRetries = Configuration.MaxRetries > 0 ? Configuration.MaxRetries : 2;

            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .Or<TimeoutException>()
                .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    maxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                                  + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        Logger.LogWarning(
                            "Retry {RetryCount}/{MaxRetries} for {Provider}. Waiting {Delay}ms. Exception: {Exception}",
                            retryCount,
                            maxRetries,
                            ProviderName,
                            timespan.TotalMilliseconds,
                            outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                    });
        }

        private AsyncCircuitBreakerPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy()
        {
            var circuitBreakerFailures = Configuration.CircuitBreakerFailuresBeforeBreaking > 0 ?
                Configuration.CircuitBreakerFailuresBeforeBreaking : 5;
            var circuitBreakerDuration = Configuration.CircuitBreakerDurationSeconds > 0 ?
                Configuration.CircuitBreakerDurationSeconds : 30;

            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TimeoutException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .CircuitBreakerAsync(
                    circuitBreakerFailures,
                    TimeSpan.FromSeconds(circuitBreakerDuration),
                    onBreak: (outcome, timespan) =>
                    {
                        Logger.LogError(
                            "Circuit breaker opened for {Provider}. Duration: {Duration}s. Reason: {Reason}",
                            ProviderName,
                            timespan.TotalSeconds,
                            outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                    },
                    onReset: () =>
                    {
                        Logger.LogInformation("Circuit breaker reset for {Provider}", ProviderName);
                    },
                    onHalfOpen: () =>
                    {
                        Logger.LogInformation("Circuit breaker half-open for {Provider}", ProviderName);
                    });
        }

        private AsyncTimeoutPolicy CreateTimeoutPolicy()
        {
            var timeoutSeconds = Configuration.TimeoutSeconds > 0 ? Configuration.TimeoutSeconds : 30;
            return Policy.TimeoutAsync(
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeoutStrategy.Optimistic);
        }

        protected virtual void ConfigureHttpClient()
        {
            if (!string.IsNullOrEmpty(Configuration.ApiKey))
            {
                ConfigureAuthentication();
            }

            if (!string.IsNullOrEmpty(Configuration.BaseUrl))
            {
                try
                {
                    HttpClient.BaseAddress = new Uri(Configuration.BaseUrl);
                }
                catch (UriFormatException ex)
                {
                    Logger.LogError(ex, "Invalid BaseUrl for {Provider}: {BaseUrl}", ProviderName, Configuration.BaseUrl);
                }
            }

            var timeoutSeconds = (Configuration.TimeoutSeconds > 0 ? Configuration.TimeoutSeconds : 30) + 5;
            HttpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            HttpClient.DefaultRequestHeaders.Clear();

            HttpClient.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");

            HttpClient.DefaultRequestHeaders.Add("X-Request-ID", Guid.NewGuid().ToString());
        }

        protected virtual void ConfigureCapabilities()
        {
            Capabilities.TextCompletion = true;
            Capabilities.MaxTokensLimit = 4000;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected abstract void ConfigureAuthentication();

        protected virtual JsonSerializerOptions CreateJsonSerializerOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }

        public abstract Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default);

        public virtual bool CanHandleRequest(AiRequest request)
        {
            if (!IsEnabled)
                return false;

            if (!IsCompatibleRequestType(request.Type))
                return false;

            if (request.MaxTokens > Capabilities.MaxTokensLimit)
                return false;

            if (!Capabilities.SupportedLanguages.Contains("en"))
                return false;

            if (request.NeedsImageGeneration && !SupportsImages)
                return false;
            if (request.NeedsTextToSpeech && !SupportsTextToSpeech)
                return false;
            if (request.NeedsTranscription && !SupportsTranscription)
                return false;
            if (request.ImageData != null && !SupportsVision)
                return false;
            if (request.AudioData != null && !SupportsAudio)
                return false;

            return true;
        }

        protected virtual bool IsCompatibleRequestType(RequestType requestType)
        {
            return requestType switch
            {
                RequestType.TextCompletion => Capabilities.TextCompletion,
                RequestType.ImageGeneration => Capabilities.ImageGeneration,
                RequestType.VisionAnalysis => Capabilities.ImageAnalysis,
                RequestType.TextToSpeech => Capabilities.TextToSpeech,
                RequestType.AudioTranscription => Capabilities.AudioTranscription,
                _ => false
            };
        }

        protected virtual async Task<HttpResponseMessage> SendWithResilienceAsync(
            Func<Task<HttpResponseMessage>> action,
            CancellationToken cancellationToken)
        {
            try
            {
                var policyWrap = Policy.WrapAsync(
                    TimeoutPolicy.AsAsyncPolicy<HttpResponseMessage>(),
                    CircuitBreakerPolicy,
                    RetryPolicy);

                return await policyWrap.ExecuteAsync(async (ct) => await action(), cancellationToken);
            }
            catch (BrokenCircuitException)
            {
                throw new CircuitBreakerOpenException(
                    ProviderName,
                    TimeSpan.FromSeconds(Configuration.CircuitBreakerDurationSeconds));
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Request to {ProviderName} timed out after {Configuration.TimeoutSeconds} seconds");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Request to {Provider} failed with resilience policies", ProviderName);
                throw;
            }
        }

        protected virtual void RecordMetrics(bool success, TimeSpan duration, string errorCode = null)
        {
            var metric = PerformanceMetrics.GetOrAdd(ProviderName, _ => new ProviderPerformance
            {
                Provider = ProviderName,
                LastUsed = DateTime.UtcNow
            });

            metric.TotalRequests++;

            if (success)
            {
                metric.SuccessfulRequests++;
                metric.TotalProcessingTime += duration;
                if (metric.SuccessfulRequests > 0)
                {
                    metric.AverageResponseTimeMs = metric.TotalProcessingTime.TotalMilliseconds / metric.SuccessfulRequests;
                }
            }
            else
            {
                metric.FailedRequests++;
                if (!string.IsNullOrEmpty(errorCode))
                {
                    metric.ErrorCounts[errorCode] = metric.ErrorCounts.GetValueOrDefault(errorCode) + 1;
                }
            }

            metric.LastUsed = DateTime.UtcNow;
        }

        public virtual bool ShouldFallback(Exception exception)
        {
            return exception switch
            {
                ProviderQuotaExceededException => true,
                RateLimitExceededException => true,
                CircuitBreakerOpenException => true,
                HttpRequestException httpEx => IsRetryableHttpException(httpEx),
                TimeoutException => true,
                TaskCanceledException => true,
                _ => false
            };
        }

        private bool IsRetryableHttpException(HttpRequestException ex)
        {
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("429") || message.Contains("503") || message.Contains("502") || message.Contains("504") || message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("capacity");
        }

        protected virtual long EstimateTokenUsage(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var characters = text.Length;

            var tokensFromWords = (long)(words * 1.3);
            var tokensFromChars = characters / 4;

            return Math.Max(tokensFromWords, tokensFromChars);
        }
    }
}