using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace DictionaryImporter.AI.Orchestration.Providers.Base
{
    public abstract class AiProviderBase : ICompletionProvider, IDisposable
    {
        protected readonly HttpClient HttpClient;
        protected readonly ILogger Logger;
        protected readonly ProviderConfiguration Configuration;
        protected readonly IQuotaManager QuotaManager;
        protected readonly IAuditLogger AuditLogger;
        protected readonly IResponseCache ResponseCache;
        protected readonly IPerformanceMetricsCollector MetricsCollector;
        protected readonly IApiKeyManager ApiKeyManager;

        protected readonly ProviderCapabilities Capabilities;
        protected readonly JsonSerializerOptions JsonSerializerOptions;

        protected readonly AsyncRetryPolicy<HttpResponseMessage> RetryPolicy;

        protected readonly AsyncCircuitBreakerPolicy<HttpResponseMessage> CircuitBreakerPolicy;
        protected readonly AsyncTimeoutPolicy TimeoutPolicy;

        protected readonly ConcurrentDictionary<string, ProviderPerformance> PerformanceMetrics;

        private readonly string _cachePrefix;

        private bool _disposed;

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

        protected AiProviderBase(
            HttpClient httpClient,
            ILogger logger,
            IOptions<ProviderConfiguration> configuration,
            IQuotaManager quotaManager = null,
            IAuditLogger auditLogger = null,
            IResponseCache responseCache = null,
            IPerformanceMetricsCollector metricsCollector = null,
            IApiKeyManager apiKeyManager = null)
        {
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

            QuotaManager = quotaManager;
            AuditLogger = auditLogger;
            ResponseCache = responseCache;
            MetricsCollector = metricsCollector;
            ApiKeyManager = apiKeyManager;

            Capabilities = new ProviderCapabilities();
            PerformanceMetrics = new ConcurrentDictionary<string, ProviderPerformance>();
            _cachePrefix = $"{ProviderName.ToLowerInvariant()}_";

            JsonSerializerOptions = CreateJsonSerializerOptions();

            RetryPolicy = CreateRetryPolicy();
            CircuitBreakerPolicy = CreateCircuitBreakerPolicy();
            TimeoutPolicy = CreateTimeoutPolicy();

            ConfigureHttpClient();
            ConfigureCapabilities();
            ConfigureAuthentication();

            Logger.LogDebug("Provider {ProviderName} initialized", ProviderName);
        }

        public virtual async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            AiResponse response = null;

            try
            {
                ValidateRequest(request);

                if (!IsEnabled)
                    throw new InvalidOperationException($"{ProviderName} provider is disabled");

                var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
                if (!quotaCheck.CanProceed)
                    throw new ProviderQuotaExceededException(ProviderName,
                        $"Quota exceeded. Remaining: {quotaCheck.RemainingRequests} requests, " +
                        $"{quotaCheck.RemainingTokens} tokens. Resets in {quotaCheck.TimeUntilReset.TotalMinutes:F0} minutes.");

                if (Configuration.EnableCaching)
                {
                    var cachedResponse = await TryGetCachedResponseAsync(request);
                    if (cachedResponse != null)
                        return cachedResponse;
                }

                var payload = CreateRequestPayload(request);

                var httpRequest = CreateHttpRequest(payload);
                var httpResponse = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseResponse(content, out var tokenUsage);

                stopwatch.Stop();

                response = CreateSuccessResponse(result, tokenUsage, stopwatch.Elapsed);

                await RecordUsageAsync(request, response, stopwatch.Elapsed, request.Context?.UserId);

                if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
                {
                    await CacheResponseAsync(request, response,
                        TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "{Provider} provider failed for request {RequestId}",
                    ProviderName, request.Context?.RequestId);

                if (ShouldFallback(ex))
                    throw;

                response = CreateErrorResponse(ex, stopwatch.Elapsed, request);

                if (AuditLogger != null)
                {
                    var auditEntry = CreateAuditEntry(request, response, stopwatch.Elapsed, request.Context?.UserId);
                    auditEntry.ErrorCode = response.ErrorCode;
                    auditEntry.ErrorMessage = response.ErrorMessage;
                    await AuditLogger.LogRequestAsync(auditEntry);
                }

                return response;
            }
            finally
            {
                if (response?.IsSuccess == true)
                {
                    RecordMetrics(true, stopwatch.Elapsed);
                }
                else if (response != null)
                {
                    RecordMetrics(false, stopwatch.Elapsed, response.ErrorCode);
                }
            }
        }

        protected abstract object CreateRequestPayload(AiRequest request);

        protected abstract string ParseResponse(string jsonResponse, out long tokenUsage);

        protected abstract void ConfigureAuthentication();

        protected virtual void ConfigureCapabilities()
        {
            Capabilities.TextCompletion = true;
            Capabilities.MaxTokensLimit = 4000;
            Capabilities.SupportedLanguages.Add("en");
        }

        protected virtual void ConfigureHttpClient()
        {
            if (!string.IsNullOrEmpty(Configuration.BaseUrl))
            {
                try
                {
                    HttpClient.BaseAddress = new Uri(Configuration.BaseUrl);
                }
                catch (UriFormatException ex)
                {
                    Logger.LogError(ex, "Invalid BaseUrl for {Provider}: {BaseUrl}",
                        ProviderName, Configuration.BaseUrl);
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

        protected virtual JsonSerializerOptions CreateJsonSerializerOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }

        protected virtual HttpRequestMessage CreateHttpRequest(object payload)
        {
            var url = Configuration.BaseUrl ?? GetDefaultBaseUrl();
            var json = JsonSerializer.Serialize(payload, JsonSerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            return new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
        }

        protected virtual string GetDefaultBaseUrl()
        {
            throw new NotImplementedException($"Default base URL not defined for {ProviderName}");
        }

        protected virtual void ValidateRequest(AiRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("Prompt cannot be empty");

            if (request.MaxTokens > Capabilities.MaxTokensLimit)
            {
                Logger.LogWarning(
                    "Requested {Requested} tokens exceeds provider limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, Capabilities.MaxTokensLimit, Capabilities.MaxTokensLimit);
            }

            if (!string.IsNullOrEmpty(request.Context?.Language))
            {
                var requestedLanguage = request.Context.Language.ToLowerInvariant();
                if (!Capabilities.SupportedLanguages.Exists(l =>
                    l.ToLowerInvariant() == requestedLanguage))
                {
                    throw new ArgumentException(
                        $"Language '{request.Context.Language}' not supported by {ProviderName}");
                }
            }
        }

        protected virtual AiResponse CreateSuccessResponse(
            string content,
            long tokensUsed,
            TimeSpan elapsedTime)
        {
            var model = Configuration.Model ?? GetDefaultModel();
            var estimatedCost = EstimateCost(tokensUsed, 0);

            return new AiResponse
            {
                Content = content.Trim(),
                Provider = ProviderName,
                Model = model,
                TokensUsed = tokensUsed,
                ProcessingTime = elapsedTime,
                IsSuccess = true,
                EstimatedCost = estimatedCost,
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["tokens_used"] = tokensUsed,
                    ["estimated_cost"] = estimatedCost,
                    [ProviderName.ToLowerInvariant()] = true
                }
            };
        }

        protected virtual AiResponse CreateErrorResponse(
            Exception ex,
            TimeSpan elapsedTime,
            AiRequest request = null)
        {
            var model = Configuration.Model ?? GetDefaultModel();
            var errorCode = ErrorHandlingHelper.GetStandardizedErrorCode(ex);
            var userFriendlyMessage = ErrorHandlingHelper.GetUserFriendlyErrorMessage(errorCode, ex);

            if (ErrorHandlingHelper.ShouldLogAsWarning(errorCode))
            {
                Logger.LogWarning(ex, "{Provider} request failed with {ErrorCode}", ProviderName, errorCode);
            }
            else if (ErrorHandlingHelper.ShouldLogAsError(errorCode))
            {
                Logger.LogError(ex, "{Provider} request failed with {ErrorCode}", ProviderName, errorCode);
            }
            else
            {
                Logger.LogDebug(ex, "{Provider} request failed with {ErrorCode}", ProviderName, errorCode);
            }

            return new AiResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                Model = model,
                ProcessingTime = elapsedTime,
                IsSuccess = false,
                ErrorCode = errorCode,
                ErrorMessage = userFriendlyMessage,
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["error_type"] = ex.GetType().Name,
                    ["technical_message"] = ex.Message,
                    ["stack_trace"] = ex.StackTrace ?? string.Empty,
                    ["is_retryable"] = ErrorHandlingHelper.IsRetryableError(errorCode)
                }
            };
        }

        protected virtual string GetDefaultModel()
        {
            throw new NotImplementedException($"Default model not defined for {ProviderName}");
        }

        protected virtual decimal EstimateCost(long inputTokens, long outputTokens)
        {
            return (inputTokens * 0.000001m) + (outputTokens * 0.000002m);
        }

        private AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy()
        {
            var maxRetries = Configuration.MaxRetries > 0 ? Configuration.MaxRetries : 2;

            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .Or<TimeoutException>()
                .OrResult(r => (int)r.StatusCode >= 500 ||
                               r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    maxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) +
                                   TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        Logger.LogWarning(
                            "Retry {RetryCount}/{MaxRetries} for {Provider}. Waiting {Delay}ms. Exception: {Exception}",
                            retryCount, maxRetries, ProviderName, timespan.TotalMilliseconds,
                            outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                    });
        }

        private AsyncCircuitBreakerPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy()
        {
            var circuitBreakerFailures = Configuration.CircuitBreakerFailuresBeforeBreaking > 0
                ? Configuration.CircuitBreakerFailuresBeforeBreaking : 5;
            var circuitBreakerDuration = Configuration.CircuitBreakerDurationSeconds > 0
                ? Configuration.CircuitBreakerDurationSeconds : 30;

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
                            ProviderName, timespan.TotalSeconds,
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
                throw new TimeoutException(
                    $"Request to {ProviderName} timed out after {Configuration.TimeoutSeconds} seconds");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Request to {Provider} failed with resilience policies", ProviderName);
                throw;
            }
        }

        protected virtual async Task<QuotaCheckResult> CheckQuotaAsync(
            AiRequest request,
            string userId = null)
        {
            if (QuotaManager == null)
                return new QuotaCheckResult { CanProceed = true };

            var estimatedTokens = EstimateTokenUsage(request.Prompt);
            var estimatedCost = EstimateCost(estimatedTokens, request.MaxTokens);

            return await QuotaManager.CheckQuotaAsync(
                ProviderName,
                userId,
                (int)estimatedTokens,
                estimatedCost);
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

        protected virtual async Task RecordUsageAsync(
            AiRequest request,
            AiResponse response,
            TimeSpan duration,
            string userId = null)
        {
            if (QuotaManager != null)
            {
                await QuotaManager.RecordUsageAsync(
                    ProviderName,
                    userId,
                    (int)response.TokensUsed,
                    response.EstimatedCost,
                    response.IsSuccess);
            }

            if (AuditLogger != null)
            {
                var auditEntry = CreateAuditEntry(request, response, duration, userId);
                await AuditLogger.LogRequestAsync(auditEntry);
            }

            if (MetricsCollector != null && response.IsSuccess)
            {
                var metrics = new ProviderMetrics
                {
                    ProviderName = ProviderName,
                    MetricDate = DateTime.UtcNow.Date,
                    TotalRequests = 1,
                    SuccessfulRequests = 1,
                    TokensUsed = response.TokensUsed,
                    DurationMs = (long)duration.TotalMilliseconds,
                    EstimatedCost = response.EstimatedCost
                };

                await MetricsCollector.RecordMetricsAsync(metrics);
            }
        }

        protected virtual AuditLogEntry CreateAuditEntry(
            AiRequest request,
            AiResponse response,
            TimeSpan duration,
            string userId = null)
        {
            return new AuditLogEntry
            {
                RequestId = request.Context?.RequestId ?? Guid.NewGuid().ToString(),
                ProviderName = ProviderName,
                Model = response.Model,
                UserId = userId ?? request.Context?.UserId,
                SessionId = request.Context?.SessionId,
                RequestType = request.Type,
                PromptHash = ComputePromptHash(request.Prompt),
                PromptLength = request.Prompt?.Length ?? 0,
                ResponseLength = response.Content?.Length ?? 0,
                TokensUsed = (int)response.TokensUsed,
                DurationMs = (int)duration.TotalMilliseconds,
                EstimatedCost = response.EstimatedCost,
                Success = response.IsSuccess,
                StatusCode = response.IsSuccess ? 200 : 500,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                RequestMetadata = request.Metadata ?? new Dictionary<string, object>(),
                ResponseMetadata = response.Metadata ?? new Dictionary<string, object>()
            };
        }

        protected virtual string ComputePromptHash(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
                return string.Empty;

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(prompt));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        protected virtual async Task<AiResponse> TryGetCachedResponseAsync(AiRequest request)
        {
            if (ResponseCache == null || !Configuration.EnableCaching)
                return null;

            var cacheKey = GenerateCacheKey(request);
            var cached = await ResponseCache.GetCachedResponseAsync(cacheKey);

            if (cached != null)
            {
                Logger.LogDebug("Cache hit for {Provider} with key {CacheKey}", ProviderName, cacheKey);

                return new AiResponse
                {
                    Content = cached.ResponseText,
                    Provider = ProviderName,
                    Model = cached.Model,
                    TokensUsed = cached.TokensUsed,
                    ProcessingTime = TimeSpan.FromMilliseconds(cached.DurationMs),
                    IsSuccess = true,
                    Metadata = new Dictionary<string, object>(cached.Metadata)
                    {
                        ["cached"] = true,
                        ["cache_hit_count"] = cached.HitCount,
                        ["cached_at"] = cached.CreatedAt
                    }
                };
            }

            return null;
        }

        protected virtual async Task CacheResponseAsync(
            AiRequest request,
            AiResponse response,
            TimeSpan ttl)
        {
            if (ResponseCache == null || !Configuration.EnableCaching)
                return;

            var cacheKey = GenerateCacheKey(request);
            var cachedResponse = new CachedResponse
            {
                CacheKey = cacheKey,
                ProviderName = ProviderName,
                Model = response.Model,
                ResponseText = response.Content,
                Metadata = response.Metadata ?? new Dictionary<string, object>(),
                TokensUsed = (int)response.TokensUsed,
                DurationMs = (int)response.ProcessingTime.TotalMilliseconds,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(ttl)
            };

            await ResponseCache.SetCachedResponseAsync(cacheKey, cachedResponse, ttl);
        }

        protected virtual string GenerateCacheKey(AiRequest request)
        {
            var keyParts = new List<string>
            {
                _cachePrefix,
                Configuration.Model?.ToLowerInvariant() ?? "default",
                ComputePromptHash(request.Prompt),
                request.MaxTokens.ToString(),
                request.Temperature.ToString("F2")
            };

            if (request.AdditionalParameters != null)
            {
                var paramHash = JsonSerializer.Serialize(request.AdditionalParameters, JsonSerializerOptions);
                keyParts.Add(ComputePromptHash(paramHash));
            }

            return string.Join("_", keyParts);
        }

        protected virtual void RecordMetrics(bool success, TimeSpan duration, string errorCode = null)
        {
            var metric = PerformanceMetrics.GetOrAdd(ProviderName, _ => new ProviderPerformance
            {
                Provider = ProviderName,
                LastUsed = DateTime.UtcNow
            });

            metric.TotalRequests++;
            metric.LastUsed = DateTime.UtcNow;

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
            return message.Contains("429") ||
                   message.Contains("503") ||
                   message.Contains("502") ||
                   message.Contains("504") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("capacity");
        }

        public virtual bool CanHandleRequest(AiRequest request)
        {
            if (!IsEnabled)
                return false;

            if (!IsCompatibleRequestType(request.Type))
                return false;

            if (request.MaxTokens > Capabilities.MaxTokensLimit)
                return false;

            if (!string.IsNullOrEmpty(request.Context?.Language) &&
                !Capabilities.SupportedLanguages.Exists(l =>
                    l.ToLowerInvariant() == request.Context.Language.ToLowerInvariant()))
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

        protected virtual string GetApiKey()
        {
            if (ApiKeyManager != null)
            {
                try
                {
                    return ApiKeyManager.GetCurrentApiKeyAsync(ProviderName).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to get API key from manager for {Provider}", ProviderName);
                }
            }

            return Configuration.ApiKey;
        }

        protected virtual string GetErrorCode(Exception ex)
        {
            return ErrorHandlingHelper.GetStandardizedErrorCode(ex);
        }

        protected virtual void EnsureProviderEnabled()
        {
            if (!IsEnabled)
                throw new InvalidOperationException($"{ProviderName} provider is disabled");
        }

        protected virtual async Task CheckAndEnforceQuotaAsync(AiRequest request)
        {
            var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
            if (!quotaCheck.CanProceed)
                throw new ProviderQuotaExceededException(ProviderName,
                    $"Quota exceeded. Remaining: {quotaCheck.RemainingRequests} requests, " +
                    $"{quotaCheck.RemainingTokens} tokens. Resets in {quotaCheck.TimeUntilReset.TotalMinutes:F0} minutes.");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    HttpClient?.Dispose();
                }

                _disposed = true;
            }
        }

        ~AiProviderBase()
        {
            Dispose(false);
        }
    }
}