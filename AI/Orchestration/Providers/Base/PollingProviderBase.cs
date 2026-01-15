using DictionaryImporter.AI.Infrastructure;

namespace DictionaryImporter.AI.Orchestration.Providers.Base
{
    public abstract class PollingProviderBase(
        HttpClient httpClient,
        ILogger logger,
        IOptions<ProviderConfiguration> configuration,
        IQuotaManager quotaManager = null,
        IAuditLogger auditLogger = null,
        IResponseCache responseCache = null,
        IPerformanceMetricsCollector metricsCollector = null,
        IApiKeyManager apiKeyManager = null)
        : AiProviderBase(httpClient, logger, configuration, quotaManager, auditLogger, responseCache, metricsCollector,
            apiKeyManager)
    {
        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ValidateRequest(request);
                EnsureProviderEnabled();
                await CheckAndEnforceQuotaAsync(request);

                var cached = await TryGetCachedResponseAsync(request);
                if (cached != null) return cached;

                var jobId = await SubmitJobAsync(request, cancellationToken);

                var result = await PollForResultAsync(jobId, cancellationToken);

                stopwatch.Stop();
                var tokenUsage = EstimateTokenUsage(result);

                var response = CreateSuccessResponse(result, tokenUsage, stopwatch.Elapsed);
                await RecordUsageAsync(request, response, stopwatch.Elapsed, request.Context?.UserId);

                if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
                {
                    await CacheResponseAsync(request, response, TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "{Provider} failed", ProviderName);

                if (ShouldFallback(ex)) throw;
                return CreateErrorResponse(ex, stopwatch.Elapsed, request);
            }
        }

        protected abstract Task<string> SubmitJobAsync(AiRequest request, CancellationToken cancellationToken);

        protected abstract Task<string> PollForResultAsync(string jobId, CancellationToken cancellationToken);

        protected virtual TimeSpan PollingInterval => TimeSpan.FromSeconds(2);

        protected virtual int MaxPollingAttempts => 30;

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new { };
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;
            return string.Empty;
        }
    }
}