using System.Data;
using ProviderMetrics = DictionaryImporter.AI.Core.Models.ProviderMetrics;

namespace DictionaryImporter.AI.Infrastructure;

public interface IQuotaManager
{
    Task<QuotaCheckResult> CheckQuotaAsync(
        string providerName,
        string userId = null,
        int estimatedTokens = 0,
        decimal estimatedCost = 0,
        CancellationToken cancellationToken = default);

    Task<QuotaUsageResult> RecordUsageAsync(
        string providerName,
        string userId = null,
        int tokensUsed = 0,
        decimal costUsed = 0,
        bool success = true,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<QuotaStatus>> GetProviderQuotasAsync(
        string providerName,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<QuotaStatus>> GetUserQuotasAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task ResetExpiredQuotasAsync(CancellationToken cancellationToken = default);
}

public interface IAuditLogger
{
    Task LogRequestAsync(AuditLogEntry entry);

    Task<IEnumerable<AuditLogEntry>> GetRecentRequestsAsync(
        string providerName = null,
        string userId = null,
        int limit = 100);

    Task<IEnumerable<AuditSummary>> GetAuditSummaryAsync(DateTime from, DateTime to);
}

public interface IPerformanceMetricsCollector
{
    Task RecordMetricsAsync(ProviderMetrics metrics);

    Task<ProviderPerformance> GetProviderPerformanceAsync(
        string providerName,
        DateTime from,
        DateTime to);

    Task<IEnumerable<ProviderPerformance>> GetAllProvidersPerformanceAsync(DateTime from, DateTime to);
}

public interface IResponseCache
{
    Task<CachedResponse> GetCachedResponseAsync(string cacheKey);

    Task SetCachedResponseAsync(string cacheKey, CachedResponse response, TimeSpan ttl);

    Task RemoveCachedResponseAsync(string cacheKey);

    Task CleanExpiredCacheAsync();
}

public interface IApiKeyManager
{
    Task<string> GetCurrentApiKeyAsync(string providerName);

    Task<string> GetApiKeyAsync(string providerName, bool useBackup = false);

    Task RotateApiKeyAsync(string providerName);

    Task<IEnumerable<ApiKeyInfo>> GetApiKeyHistoryAsync(string providerName);

    Task<bool> ValidateApiKeyAsync(string providerName, string apiKey);
}