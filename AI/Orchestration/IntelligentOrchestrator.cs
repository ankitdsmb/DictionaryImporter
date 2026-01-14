using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Core.Exceptions;

namespace DictionaryImporter.AI.Orchestration
{
    public class IntelligentOrchestrator : ICompletionOrchestrator, IDisposable
    {
        private readonly IProviderFactory _providerFactory;
        private readonly ILogger<IntelligentOrchestrator> _logger;
        private readonly OrchestrationConfiguration _configuration;
        private readonly ConcurrentDictionary<string, ProviderHealth> _providerHealth;
        private readonly ConcurrentDictionary<string, ProviderPerformance> _performanceMetrics;
        private readonly List<ProviderFailure> _recentFailures;
        private readonly object _failureLock = new();
        private readonly SemaphoreSlim _rateLimitSemaphore;
        private readonly Stopwatch _uptimeTimer;
        private readonly object _metricsLock = new();

        private long _totalRequests;
        private long _successfulRequests;
        private long _failedRequests;
        private double _totalResponseTimeMs;
        private DateTime _lastPerformanceUpdate = DateTime.UtcNow;

        public IntelligentOrchestrator(
            IProviderFactory providerFactory,
            ILogger<IntelligentOrchestrator> logger,
            IOptions<OrchestrationConfiguration> configuration)
        {
            _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

            _providerHealth = new ConcurrentDictionary<string, ProviderHealth>();
            _performanceMetrics = new ConcurrentDictionary<string, ProviderPerformance>();
            _recentFailures = new List<ProviderFailure>();

            var maxConcurrent = _configuration.MaxConcurrentRequests > 0 ? _configuration.MaxConcurrentRequests : 5;
            _rateLimitSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

            _uptimeTimer = Stopwatch.StartNew();

            InitializeProviders();

            _logger.LogInformation(
                "IntelligentOrchestrator initialized with {MaxConcurrentRequests} concurrent requests limit",
                maxConcurrent);
        }

        private void InitializeProviders()
        {
            var providers = _providerFactory.GetAllProviders();
            foreach (var provider in providers)
            {
                _providerHealth[provider.ProviderName] = new ProviderHealth
                {
                    ProviderName = provider.ProviderName,
                    IsHealthy = true,
                    LastHealthCheck = DateTime.UtcNow
                };
            }
        }

        public async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var requestStopwatch = Stopwatch.StartNew();
            var requestContext = request.Context;
            var attemptedProviders = new List<string>();

            _logger.LogInformation(
                "Processing AI request {RequestId} of type {RequestType} for user {UserId}",
                requestContext.RequestId,
                request.Type,
                requestContext.UserId);

            try
            {
                if ((DateTime.UtcNow - _lastPerformanceUpdate).TotalMinutes > 5)
                {
                    UpdatePerformanceCache();
                    _lastPerformanceUpdate = DateTime.UtcNow;
                }

                if (!await _rateLimitSemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                {
                    throw new TimeoutException("Could not acquire semaphore for concurrent request limit");
                }

                try
                {
                    Interlocked.Increment(ref _totalRequests);

                    var suitableProviders = SelectSuitableProviders(request).ToList();
                    if (!suitableProviders.Any())
                    {
                        throw new NoAvailableProvidersException(request.Type);
                    }

                    var attemptedProvidersList = new List<string>();
                    Exception lastException = null;
                    AiResponse lastFailedResponse = null;

                    foreach (var provider in suitableProviders)
                    {
                        attemptedProvidersList.Add(provider.ProviderName);

                        try
                        {
                            _logger.LogDebug(
                                "Attempting provider {Provider} (Priority: {Priority}) for request {RequestId}",
                                provider.ProviderName,
                                provider.Priority,
                                requestContext.RequestId);

                            var response = await provider.GetCompletionAsync(request, cancellationToken);

                            if (response.IsSuccess)
                            {
                                RecordSuccess(provider.ProviderName, requestStopwatch.Elapsed);
                                response.AttemptedProviders = attemptedProvidersList;
                                response.RetryCount = attemptedProvidersList.Count - 1;

                                _logger.LogInformation(
                                    "Request {RequestId} completed successfully using {Provider} " +
                                    "(tokens: {Tokens}, time: {Time:F0}ms, priority: {Priority})",
                                    requestContext.RequestId,
                                    provider.ProviderName,
                                    response.TokensUsed,
                                    response.ProcessingTime.TotalMilliseconds,
                                    provider.Priority);

                                return response;
                            }

                            lastFailedResponse = response;
                            lastException = new InvalidOperationException(response.ErrorMessage);
                            RecordFailure(provider.ProviderName, lastException, $"Provider returned error: {response.ErrorMessage}");
                        }
                        catch (Exception ex) when (ShouldFallback(ex, provider))
                        {
                            lastException = ex;
                            RecordFailure(provider.ProviderName, ex, ex.Message);

                            if (_configuration.EnableFallback)
                            {
                                _logger.LogWarning(
                                    "Provider {Provider} failed ({Error}), attempting next provider",
                                    provider.ProviderName,
                                    ex.Message);
                                continue;
                            }
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            RecordFailure(provider.ProviderName, ex, "Non-retryable error");
                            _logger.LogError(ex, "Critical error in provider {Provider}", provider.ProviderName);
                            break;
                        }
                    }

                    var errorMessage = BuildErrorMessage(attemptedProvidersList, lastException, lastFailedResponse);
                    _logger.LogError(lastException, errorMessage);

                    return new AiResponse
                    {
                        Content = string.Empty,
                        Provider = "None",
                        ProcessingTime = requestStopwatch.Elapsed,
                        IsSuccess = false,
                        ErrorMessage = errorMessage,
                        Metadata = new Dictionary<string, object>
                        {
                            { "attempted_providers", attemptedProvidersList },
                            { "total_attempts", attemptedProvidersList.Count },
                            { "last_exception", lastException?.GetType().Name ?? "None" }
                        }
                    };
                }
                finally
                {
                    _rateLimitSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Request {RequestId} failed after trying {AttemptCount} providers: {Error}",
                    requestContext.RequestId,
                    attemptedProviders.Count,
                    ex.Message);

                return CreateErrorResponse(request, attemptedProviders, ex);
            }
            finally
            {
                requestStopwatch.Stop();
                RecordMetrics(requestStopwatch.Elapsed);
            }
        }

        private IEnumerable<ICompletionProvider> SelectSuitableProviders(AiRequest request)
        {
            var providers = _providerFactory.GetAllProviders()
                .Where(p => p.CanHandleRequest(request))
                .ToList();

            var scoredProviders = providers.Select(p => new
            {
                Provider = p,
                Score = CalculateProviderScore(p, request)
            });

            return scoredProviders
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Provider.Priority)
                .Select(x => x.Provider);
        }

        private double CalculateProviderScore(ICompletionProvider provider, AiRequest request)
        {
            double score = 100.0;

            if (_performanceMetrics.TryGetValue(provider.ProviderName, out var performance))
            {
                score += (performance.SuccessfulRequests * 100.0 / Math.Max(1, performance.TotalRequests)) * 0.4;

                if (performance.AverageResponseTimeMs > 0)
                {
                    var timeScore = Math.Max(0, 20 - (performance.AverageResponseTimeMs / 100));
                    score += timeScore;
                }

                var hoursSinceLastUse = (DateTime.UtcNow - performance.LastUsed).TotalHours;
                var recencyBonus = Math.Max(0, 10 - (hoursSinceLastUse / 2));
                score += recencyBonus;
            }

            score += (100 - provider.Priority) * 0.1;

            if (provider.IsLocal)
                score += 15;

            if (request.NeedsImageGeneration && provider.SupportsImages)
                score += 25;
            if (request.NeedsTextToSpeech && provider.SupportsTextToSpeech)
                score += 25;
            if (request.NeedsTranscription && provider.SupportsTranscription)
                score += 25;

            return score;
        }

        private bool ShouldFallback(Exception exception, ICompletionProvider provider)
        {
            return provider.ShouldFallback(exception);
        }

        private void RecordSuccess(string providerName, TimeSpan responseTime)
        {
            lock (_metricsLock)
            {
                _successfulRequests++;
                _totalResponseTimeMs += responseTime.TotalMilliseconds;

                var metric = _performanceMetrics.GetOrAdd(providerName, _ => new ProviderPerformance
                {
                    Provider = providerName,
                    LastUsed = DateTime.UtcNow
                });

                metric.TotalRequests++;
                metric.SuccessfulRequests++;
                metric.LastUsed = DateTime.UtcNow;

                if (metric.AverageResponseTimeMs == 0)
                    metric.AverageResponseTimeMs = responseTime.TotalMilliseconds;
                else
                    metric.AverageResponseTimeMs = (metric.AverageResponseTimeMs * 0.9) + (responseTime.TotalMilliseconds * 0.1);
            }
        }

        private void RecordFailure(string providerName, Exception exception, string errorDetail)
        {
            if (!_configuration.LogFailures)
                return;

            lock (_failureLock)
            {
                _recentFailures.Add(new ProviderFailure
                {
                    Provider = providerName,
                    Exception = exception,
                    FailureTime = DateTime.UtcNow
                });

                var metric = _performanceMetrics.GetOrAdd(providerName, _ => new ProviderPerformance
                {
                    Provider = providerName,
                    LastUsed = DateTime.UtcNow
                });

                metric.TotalRequests++;
                metric.FailedRequests++;
                metric.ErrorCounts[errorDetail] = metric.ErrorCounts.GetValueOrDefault(errorDetail) + 1;

                if (_recentFailures.Count > _configuration.FailureHistorySize)
                {
                    _recentFailures.RemoveRange(0, _recentFailures.Count - _configuration.FailureHistorySize);
                }

                var cutoff = DateTime.UtcNow.AddHours(-24);
                _recentFailures.RemoveAll(f => f.FailureTime < cutoff);
            }
        }

        private string BuildErrorMessage(
            List<string> attemptedProviders,
            Exception lastException,
            AiResponse lastFailedResponse)
        {
            if (lastFailedResponse != null && !string.IsNullOrEmpty(lastFailedResponse.ErrorMessage))
            {
                return $"All providers failed. Last error from {attemptedProviders.Last()}: " +
                       $"{lastFailedResponse.ErrorMessage}. " +
                       $"Attempted: {string.Join(", ", attemptedProviders)}";
            }

            if (lastException != null)
            {
                return $"All providers failed. Last exception: {lastException.Message}. " +
                       $"Attempted: {string.Join(", ", attemptedProviders)}";
            }

            return $"All providers failed. Attempted: {string.Join(", ", attemptedProviders)}";
        }

        private AiResponse CreateErrorResponse(
            AiRequest request,
            List<string> attemptedProviders,
            Exception exception)
        {
            return new AiResponse
            {
                Content = string.Empty,
                Provider = "None",
                IsSuccess = false,
                ErrorCode = GetErrorCode(exception),
                ErrorMessage = exception.Message,
                ProcessingTime = TimeSpan.Zero,
                AttemptedProviders = attemptedProviders,
                RetryCount = attemptedProviders.Count,
                Metadata = new Dictionary<string, object>
                {
                    ["request_id"] = request.Context.RequestId,
                    ["request_type"] = request.Type.ToString(),
                    ["exception_type"] = exception.GetType().Name,
                    ["attempted_providers_count"] = attemptedProviders.Count,
                    ["timestamp"] = DateTime.UtcNow
                }
            };
        }

        private string GetErrorCode(Exception exception)
        {
            return exception switch
            {
                AiOrchestrationException aiEx => aiEx.ErrorCode,
                HttpRequestException => "HTTP_ERROR",
                TimeoutException => "TIMEOUT",
                TaskCanceledException => "CANCELLED",
                _ => "UNKNOWN_ERROR"
            };
        }

        private void RecordMetrics(TimeSpan responseTime)
        {
            lock (_metricsLock)
            {
                _totalResponseTimeMs += responseTime.TotalMilliseconds;
            }
        }

        private void UpdatePerformanceCache()
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var oldKeys = _performanceMetrics.Keys
                .Where(k => _performanceMetrics[k].LastUsed < cutoff)
                .ToList();

            foreach (var key in oldKeys)
            {
                _performanceMetrics.TryRemove(key, out _);
            }
        }

        public async Task<OrchestrationMetrics> GetMetricsAsync()
        {
            var metrics = new OrchestrationMetrics
            {
                TotalRequests = (int)Interlocked.Read(ref _totalRequests),
                SuccessfulRequests = (int)Interlocked.Read(ref _successfulRequests),
                FailedRequests = (int)Interlocked.Read(ref _failedRequests),
                AverageResponseTimeMs = _totalRequests > 0 ? _totalResponseTimeMs / _totalRequests : 0
            };

            lock (_metricsLock)
            {
                foreach (var kvp in _performanceMetrics)
                {
                    var performance = kvp.Value;
                    metrics.ProviderMetrics[kvp.Key] = new ProviderMetrics
                    {
                        Name = performance.Provider,
                        TotalRequests = performance.TotalRequests,
                        SuccessfulRequests = performance.SuccessfulRequests,
                        FailedRequests = performance.FailedRequests,
                        AverageResponseTimeMs = performance.AverageResponseTimeMs,
                        LastUsed = performance.LastUsed,
                        IsHealthy = _providerHealth.TryGetValue(kvp.Key, out var health) && health.IsHealthy,
                        ErrorCounts = performance.ErrorCounts
                    };
                }
            }

            return await Task.FromResult(metrics);
        }

        public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var healthyProviders = 0;
                var totalProviders = 0;

                var providers = _providerFactory.GetAllProviders();
                foreach (var provider in providers.Where(p => p.IsEnabled))
                {
                    totalProviders++;

                    try
                    {
                        var testRequest = new AiRequest
                        {
                            Prompt = "Health check",
                            MaxTokens = 5,
                            Type = RequestType.TextCompletion
                        };

                        var response = await provider.GetCompletionAsync(testRequest, cancellationToken);
                        if (response.IsSuccess)
                        {
                            healthyProviders++;
                            UpdateProviderHealth(provider.ProviderName, true);
                        }
                        else
                        {
                            UpdateProviderHealth(provider.ProviderName, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Health check failed for provider {Provider}",
                            provider.ProviderName);
                        UpdateProviderHealth(provider.ProviderName, false);
                    }
                }

                var isHealthy = totalProviders > 0 && healthyProviders > totalProviders / 2;
                _logger.LogInformation(
                    "Health check completed: {Healthy}/{Total} providers healthy",
                    healthyProviders,
                    totalProviders);

                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return false;
            }
        }

        public IReadOnlyList<ProviderFailure> GetRecentFailures()
        {
            lock (_failureLock)
            {
                return _recentFailures.ToList().AsReadOnly();
            }
        }

        public IReadOnlyDictionary<string, ProviderPerformance> GetPerformanceMetrics()
        {
            lock (_metricsLock)
            {
                return new Dictionary<string, ProviderPerformance>(_performanceMetrics);
            }
        }

        public IReadOnlyList<ProviderStatus> GetProviderStatuses()
        {
            var statuses = new List<ProviderStatus>();

            foreach (var kvp in _providerHealth)
            {
                var health = kvp.Value;
                var performance = _performanceMetrics.GetValueOrDefault(kvp.Key);

                statuses.Add(new ProviderStatus
                {
                    Name = kvp.Key,
                    IsEnabled = true,
                    IsHealthy = health.IsHealthy,
                    HealthStatus = health.IsHealthy ? "Healthy" : "Unhealthy",
                    LastHealthCheck = health.LastHealthCheck,
                    Capabilities = GetProviderCapabilities(kvp.Key)
                });
            }

            return statuses.AsReadOnly();
        }

        public IEnumerable<string> GetAvailableProviders()
        {
            return _providerFactory.GetAllProviders().Select(p => p.ProviderName);
        }

        public IEnumerable<string> GetProvidersByCapability(string capability)
        {
            return _providerFactory.GetAllProviders()
                .Where(p =>
                {
                    return capability.ToLower() switch
                    {
                        "image" => p.SupportsImages,
                        "vision" => p.SupportsVision,
                        "audio" => p.SupportsAudio,
                        "tts" => p.SupportsTextToSpeech,
                        "transcription" => p.SupportsTranscription,
                        "local" => p.IsLocal,
                        _ => true
                    };
                })
                .Select(p => p.ProviderName);
        }

        private List<string> GetProviderCapabilities(string providerName)
        {
            var capabilities = new List<string>();
            var provider = _providerFactory.GetAllProviders()
                .FirstOrDefault(p => p.ProviderName == providerName);

            if (provider != null)
            {
                if (provider.SupportsAudio) capabilities.Add("Audio");
                if (provider.SupportsVision) capabilities.Add("Vision");
                if (provider.SupportsImages) capabilities.Add("Images");
                if (provider.SupportsTextToSpeech) capabilities.Add("TextToSpeech");
                if (provider.SupportsTranscription) capabilities.Add("Transcription");
                if (provider.IsLocal) capabilities.Add("Local");
            }

            return capabilities;
        }

        private void UpdateProviderHealth(string providerName, bool isHealthy)
        {
            if (_providerHealth.TryGetValue(providerName, out var health))
            {
                health.IsHealthy = isHealthy;
                health.LastHealthCheck = DateTime.UtcNow;
                health.FailureCount = isHealthy ? 0 : health.FailureCount + 1;
            }
        }

        public void Dispose()
        {
            _rateLimitSemaphore?.Dispose();
            _uptimeTimer?.Stop();
            _logger.LogInformation("IntelligentOrchestrator disposed");
        }
    }
}