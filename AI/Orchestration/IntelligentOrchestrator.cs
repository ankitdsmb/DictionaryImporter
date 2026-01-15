using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DictionaryImporter.AI.Orchestration;

public class IntelligentOrchestrator : ICompletionOrchestrator, IDisposable
{
    private readonly IProviderFactory _providerFactory;
    private readonly ILogger<IntelligentOrchestrator> _logger;
    private readonly AiOrchestrationConfiguration _configuration;
    private readonly IQuotaManager _quotaManager;
    private readonly IAuditLogger _auditLogger;
    private readonly IPerformanceMetricsCollector _metricsCollector;
    private readonly ITelemetryService _telemetryService;

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
    private DateTime _lastHealthCheck = DateTime.UtcNow;

    public IntelligentOrchestrator(
        IProviderFactory providerFactory,
        ILogger<IntelligentOrchestrator> logger,
        IOptions<AiOrchestrationConfiguration> configuration,
        IQuotaManager quotaManager = null,
        IAuditLogger auditLogger = null,
        IPerformanceMetricsCollector metricsCollector = null,
        ITelemetryService telemetryService = null)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _quotaManager = quotaManager;
        _auditLogger = auditLogger;
        _metricsCollector = metricsCollector;
        _telemetryService = telemetryService;

        _providerHealth = new ConcurrentDictionary<string, ProviderHealth>();
        _performanceMetrics = new ConcurrentDictionary<string, ProviderPerformance>();
        _recentFailures = new List<ProviderFailure>();

        var maxConcurrent = _configuration.MaxConcurrentRequests > 0
            ? _configuration.MaxConcurrentRequests : 10;
        _rateLimitSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

        _uptimeTimer = Stopwatch.StartNew();

        InitializeProviders();

        _logger.LogInformation(
            "IntelligentOrchestrator initialized with {MaxConcurrentRequests} concurrent requests limit",
            maxConcurrent);
    }

    public async Task<OrchestrationMetrics> GetMetricsAsync()
    {
        var metrics = new OrchestrationMetrics
        {
            TotalRequests = (int)Interlocked.Read(ref _totalRequests),
            SuccessfulRequests = (int)Interlocked.Read(ref _successfulRequests),
            FailedRequests = (int)Interlocked.Read(ref _failedRequests),
            Uptime = _uptimeTimer.Elapsed,
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

        if (_quotaManager != null)
        {
            try
            {
                var providers = _providerFactory.GetAllProviders().Select(p => p.ProviderName);
                foreach (var provider in providers)
                {
                    var quotas = await _quotaManager.GetProviderQuotasAsync(provider);
                    metrics.QuotaStatus[provider] = quotas.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get quota metrics");
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
                        Type = RequestType.TextCompletion,
                        Context = new RequestContext
                        {
                            RequestId = $"healthcheck-{Guid.NewGuid()}",
                            UserId = "system"
                        }
                    };

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

                    var response = await provider.GetCompletionAsync(testRequest, linkedCts.Token);

                    if (response.IsSuccess)
                    {
                        healthyProviders++;
                        UpdateProviderHealth(provider.ProviderName, true, "Health check passed");
                    }
                    else
                    {
                        UpdateProviderHealth(provider.ProviderName, false, $"Health check failed: {response.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health check failed for provider {Provider}", provider.ProviderName);
                    UpdateProviderHealth(provider.ProviderName, false, $"Exception: {ex.Message}");
                }
            }

            var isHealthy = totalProviders > 0 && healthyProviders > totalProviders / 2;

            _logger.LogInformation(
                "Health check completed: {Healthy}/{Total} providers healthy. Overall: {Status}",
                healthyProviders, totalProviders, isHealthy ? "Healthy" : "Unhealthy");

            await RecordTelemetryAsync(
                new AiRequest { Context = new RequestContext { RequestId = $"healthcheck-{Guid.NewGuid()}" } },
                TelemetryEventType.HealthCheckPerformed,
                null,
                new { HealthyProviders = healthyProviders, TotalProviders = totalProviders, IsHealthy = isHealthy });

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return false;
        }
    }

    public IReadOnlyList<ProviderStatus> GetProviderStatuses()
    {
        var statuses = new List<ProviderStatus>();

        foreach (var kvp in _providerHealth)
        {
            var health = kvp.Value;
            var performance = _performanceMetrics.GetValueOrDefault(kvp.Key);

            var status = new ProviderStatus
            {
                Name = kvp.Key,
                IsEnabled = true,
                IsHealthy = health.IsHealthy,
                HealthStatus = health.IsHealthy ? "Healthy" : "Unhealthy",
                LastHealthCheck = health.LastHealthCheck,
                LastUsed = performance?.LastUsed ?? DateTime.MinValue,
                SuccessRate = performance?.SuccessRate ?? 0,
                AverageResponseTimeMs = performance?.AverageResponseTimeMs ?? 0,
                TotalRequests = performance?.TotalRequests ?? 0,
                ErrorCount = health.FailureCount
            };

            var provider = _providerFactory.GetAllProviders()
                .FirstOrDefault(p => p.ProviderName == kvp.Key);

            if (provider != null)
            {
                status.Capabilities = GetProviderCapabilities(provider);
            }

            statuses.Add(status);
        }

        return statuses.AsReadOnly();
    }

    public IReadOnlyDictionary<string, ProviderPerformance> GetPerformanceMetrics()
    {
        lock (_metricsLock)
        {
            return new Dictionary<string, ProviderPerformance>(_performanceMetrics);
        }
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        return _providerFactory.GetAllProviders()
            .Where(p => p.IsEnabled)
            .Select(p => p.ProviderName);
    }

    public IEnumerable<string> GetProvidersByCapability(string capability)
    {
        return _providerFactory.GetAllProviders()
            .Where(p =>
            {
                return capability.ToLower() switch
                {
                    "image" or "images" => p.SupportsImages,
                    "vision" => p.SupportsVision,
                    "audio" => p.SupportsAudio,
                    "tts" or "texttospeech" => p.SupportsTextToSpeech,
                    "transcription" => p.SupportsTranscription,
                    "local" => p.IsLocal,
                    "text" or "completion" => p.Capabilities.TextCompletion,
                    "chat" => p.Capabilities.ChatCompletion,
                    _ => true
                };
            })
            .Select(p => p.ProviderName);
    }

    public IReadOnlyList<ProviderFailure> GetRecentFailures()
    {
        lock (_failureLock)
        {
            return _recentFailures.ToList().AsReadOnly();
        }
    }

    private void UpdateProviderHealth(string providerName, bool isHealthy, string message = null)
    {
        if (_providerHealth.TryGetValue(providerName, out var health))
        {
            health.IsHealthy = isHealthy;
            health.LastHealthCheck = DateTime.UtcNow;

            if (isHealthy)
            {
                health.FailureCount = 0;
                health.LastError = null;
            }
            else
            {
                health.LastError = message;
            }

            _logger.LogDebug(
                "Provider {Provider} health updated: {Status} - {Message}",
                providerName, isHealthy ? "Healthy" : "Unhealthy", message);
        }
    }

    private List<string> GetProviderCapabilities(ICompletionProvider provider)
    {
        var capabilities = new List<string>();

        if (provider.SupportsAudio) capabilities.Add("Audio");
        if (provider.SupportsVision) capabilities.Add("Vision");
        if (provider.SupportsImages) capabilities.Add("Images");
        if (provider.SupportsTextToSpeech) capabilities.Add("TextToSpeech");
        if (provider.SupportsTranscription) capabilities.Add("Transcription");
        if (provider.IsLocal) capabilities.Add("Local");
        if (provider.Capabilities.TextCompletion) capabilities.Add("TextCompletion");
        if (provider.Capabilities.ChatCompletion) capabilities.Add("ChatCompletion");
        if (provider.Capabilities.ImageGeneration) capabilities.Add("ImageGeneration");
        if (provider.Capabilities.ImageAnalysis) capabilities.Add("ImageAnalysis");
        if (provider.Capabilities.AudioTranscription) capabilities.Add("AudioTranscription");

        return capabilities;
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
                LastHealthCheck = DateTime.UtcNow,
                SuccessRate = 100,
                ResponseTimeMs = 0
            };
        }
    }

    public async Task<AiResponse> GetCompletionAsync(
    AiRequest request,
    CancellationToken cancellationToken = default)
    {
        var requestStopwatch = Stopwatch.StartNew();
        var requestContext = request.Context ?? new RequestContext();
        var attemptedProviders = new List<string>();

        if (string.IsNullOrEmpty(requestContext.RequestId))
        {
            requestContext.RequestId = Guid.NewGuid().ToString();
            request.Context = requestContext;
        }

        _logger.LogInformation(
            "Processing AI request {RequestId} of type {RequestType} for user {UserId}",
            requestContext.RequestId, request.Type, requestContext.UserId);

        await RecordTelemetryAsync(request, TelemetryEventType.RequestStarted);

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
                var suitableProviders = await SelectSuitableProvidersAsync(request, cancellationToken);

                if (!suitableProviders.Any())
                {
                    throw new NoAvailableProvidersException(request.Type);
                }

                Exception lastException = null;
                AiResponse lastFailedResponse = null;

                foreach (var provider in suitableProviders)
                {
                    attemptedProviders.Add(provider.ProviderName);

                    try
                    {
                        _logger.LogDebug(
                            "Attempting provider {Provider} (Priority: {Priority}) for request {RequestId}",
                            provider.ProviderName, provider.Priority, requestContext.RequestId);

                        var response = await provider.GetCompletionAsync(request, cancellationToken);

                        if (response.IsSuccess)
                        {
                            RecordSuccess(provider.ProviderName, requestStopwatch.Elapsed);
                            response.AttemptedProviders = attemptedProviders;
                            response.RetryCount = attemptedProviders.Count - 1;

                            _logger.LogInformation(
                                "Request {RequestId} completed successfully using {Provider} " +
                                "(tokens: {Tokens}, time: {Time:F0}ms, priority: {Priority})",
                                requestContext.RequestId, provider.ProviderName, response.TokensUsed,
                                response.ProcessingTime.TotalMilliseconds, provider.Priority);

                            await RecordTelemetryAsync(request, TelemetryEventType.RequestCompleted, provider.ProviderName, response);
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
                                provider.ProviderName, ex.Message);
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

                var errorMessage = BuildErrorMessage(attemptedProviders, lastException, lastFailedResponse);
                _logger.LogError(lastException, errorMessage);
                await RecordTelemetryAsync(request, TelemetryEventType.RequestFailed, attemptedProviders.LastOrDefault(), lastException);

                return new AiResponse
                {
                    Content = string.Empty,
                    Provider = "None",
                    ProcessingTime = requestStopwatch.Elapsed,
                    IsSuccess = false,
                    ErrorCode = GetErrorCode(lastException),
                    ErrorMessage = errorMessage,
                    Metadata = new Dictionary<string, object>
                    {
                        ["attempted_providers"] = attemptedProviders,
                        ["total_attempts"] = attemptedProviders.Count,
                        ["last_exception"] = lastException?.GetType().Name ?? "None",
                        ["last_error_message"] = lastException?.Message ?? "Unknown"
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
                ex, "Request {RequestId} failed after trying {AttemptCount} providers: {Error}",
                requestContext.RequestId, attemptedProviders.Count, ex.Message);

            await RecordTelemetryAsync(request, TelemetryEventType.RequestFailed, null, ex);
            return CreateErrorResponse(request, attemptedProviders, ex);
        }
        finally
        {
            requestStopwatch.Stop();
            RecordMetrics(requestStopwatch.Elapsed);
        }
    }

    private async Task<IEnumerable<ICompletionProvider>> SelectSuitableProvidersAsync(
        AiRequest request,
        CancellationToken cancellationToken)
    {
        var providers = _providerFactory.GetAllProviders()
            .Where(p => p.CanHandleRequest(request))
            .ToList();

        var scoredProviders = new List<(ICompletionProvider Provider, double Score)>();

        foreach (var provider in providers)
        {
            var score = CalculateProviderScore(provider, request);

            if (_quotaManager != null && _configuration.EnableQuotaManagement)
            {
                try
                {
                    var quotaCheck = await _quotaManager.CheckQuotaAsync(
                        provider.ProviderName,
                        request.Context?.UserId,
                        cancellationToken: cancellationToken);

                    if (!quotaCheck.CanProceed)
                    {
                        score -= 50;
                        _logger.LogDebug("Provider {Provider} quota exceeded, reducing score", provider.ProviderName);
                    }
                    else if (quotaCheck.IsNearLimit)
                    {
                        score -= 20;
                        _logger.LogDebug("Provider {Provider} near quota limit, reducing score", provider.ProviderName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check quota for provider {Provider}", provider.ProviderName);
                }
            }

            if (score > 0)
            {
                scoredProviders.Add((provider, score));
            }
        }

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
            var successRate = performance.TotalRequests > 0
                ? (performance.SuccessfulRequests * 100.0) / performance.TotalRequests
                : 100.0;
            score += successRate * 0.4;

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

        if (provider.IsLocal) score += 15;
        if (request.NeedsImageGeneration && provider.SupportsImages) score += 25;
        if (request.NeedsTextToSpeech && provider.SupportsTextToSpeech) score += 25;
        if (request.NeedsTranscription && provider.SupportsTranscription) score += 25;
        if (request.ImageData != null && provider.SupportsVision) score += 25;
        if (request.AudioData != null && provider.SupportsAudio) score += 25;

        if (_providerHealth.TryGetValue(provider.ProviderName, out var health) && !health.IsHealthy)
        {
            score -= 40;
        }

        return Math.Max(0, score);
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
            metric.TotalProcessingTime += responseTime;

            if (metric.SuccessfulRequests > 0)
            {
                metric.AverageResponseTimeMs = metric.TotalProcessingTime.TotalMilliseconds / metric.SuccessfulRequests;
            }

            if (_providerHealth.TryGetValue(providerName, out var health))
            {
                health.IsHealthy = true;
                health.LastHealthCheck = DateTime.UtcNow;
                health.SuccessRate = metric.SuccessRate;
                health.ResponseTimeMs = metric.AverageResponseTimeMs;
            }
        }
    }

    private void RecordFailure(string providerName, Exception exception, string errorDetail)
    {
        if (!_configuration.EnableAuditLogging)
            return;

        lock (_failureLock)
        {
            _failedRequests++;

            _recentFailures.Add(new ProviderFailure
            {
                Provider = providerName,
                Exception = exception,
                FailureTime = DateTime.UtcNow,
                ErrorDetail = errorDetail
            });

            var metric = _performanceMetrics.GetOrAdd(providerName, _ => new ProviderPerformance
            {
                Provider = providerName,
                LastUsed = DateTime.UtcNow
            });

            metric.TotalRequests++;
            metric.FailedRequests++;
            metric.ErrorCounts[errorDetail] = metric.ErrorCounts.GetValueOrDefault(errorDetail) + 1;

            if (_providerHealth.TryGetValue(providerName, out var health))
            {
                health.FailureCount++;
                health.LastFailure = DateTime.UtcNow;
                health.LastError = errorDetail;

                if (health.FailureCount >= 3)
                {
                    health.IsHealthy = false;
                }
            }

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
        var errorCode = ErrorHandlingHelper.GetStandardizedErrorCode(exception);
        var userFriendlyMessage = ErrorHandlingHelper.GetUserFriendlyErrorMessage(errorCode, exception);

        return new AiResponse
        {
            Content = string.Empty,
            Provider = "None",
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = userFriendlyMessage,
            ProcessingTime = TimeSpan.Zero,
            AttemptedProviders = attemptedProviders,
            RetryCount = attemptedProviders.Count,
            Metadata = new Dictionary<string, object>
            {
                ["request_id"] = request.Context?.RequestId,
                ["request_type"] = request.Type.ToString(),
                ["error_type"] = exception.GetType().Name,
                ["technical_message"] = exception.Message,
                ["attempted_providers_count"] = attemptedProviders.Count,
                ["timestamp"] = DateTime.UtcNow,
                ["is_retryable"] = ErrorHandlingHelper.IsRetryableError(errorCode)
            }
        };
    }

    private string GetErrorCode(Exception exception)
    {
        return ErrorHandlingHelper.GetStandardizedErrorCode(exception);
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

    private async Task RecordTelemetryAsync(
        AiRequest request,
        TelemetryEventType eventType,
        string providerName = null,
        object data = null)
    {
        if (_telemetryService == null)
            return;

        try
        {
            var telemetryEvent = new TelemetryEvent
            {
                EventType = eventType,
                RequestId = request.Context?.RequestId,
                ProviderName = providerName,
                UserId = request.Context?.UserId,
                RequestType = request.Type,
                Timestamp = DateTime.UtcNow,
                Data = data
            };

            await _telemetryService.RecordEventAsync(telemetryEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record telemetry event");
        }
    }

    public void Dispose()
    {
        _rateLimitSemaphore?.Dispose();
        _uptimeTimer?.Stop();
        _logger.LogInformation("IntelligentOrchestrator disposed after {Uptime} minutes",
            _uptimeTimer.Elapsed.TotalMinutes);
    }
}