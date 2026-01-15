namespace DictionaryImporter.AI.Infrastructure.Implementations
{
    public class NullQuotaManager(ILogger<NullQuotaManager> logger = null) : IQuotaManager
    {
        public Task<QuotaCheckResult> CheckQuotaAsync(
            string providerName,
            string userId = null,
            int estimatedTokens = 0,
            decimal estimatedCost = 0,
            CancellationToken cancellationToken = default)
        {
            logger?.LogDebug("NullQuotaManager: Checking quota for {Provider}", providerName);
            return Task.FromResult(new QuotaCheckResult
            {
                CanProceed = true,
                ProviderName = providerName,
                UserId = userId,
                RemainingRequests = int.MaxValue,
                RemainingTokens = long.MaxValue,
                RemainingCost = decimal.MaxValue,
                TimeUntilReset = TimeSpan.Zero,
                IsNearLimit = false
            });
        }

        public Task<QuotaUsageResult> RecordUsageAsync(
            string providerName,
            string userId = null,
            int tokensUsed = 0,
            decimal costUsed = 0,
            bool success = true,
            CancellationToken cancellationToken = default)
        {
            logger?.LogDebug("NullQuotaManager: Recording usage for {Provider}", providerName);
            return Task.FromResult(new QuotaUsageResult
            {
                ProviderName = providerName,
                UserId = userId,
                TokensUsed = tokensUsed,
                CostUsed = costUsed,
                Success = success,
                RecordedAt = DateTime.UtcNow
            });
        }

        public Task<IEnumerable<QuotaStatus>> GetProviderQuotasAsync(
            string providerName,
            CancellationToken cancellationToken = default)
        {
            logger?.LogDebug("NullQuotaManager: Getting quotas for provider {Provider}", providerName);
            return Task.FromResult(Enumerable.Empty<QuotaStatus>());
        }

        public Task<IEnumerable<QuotaStatus>> GetUserQuotasAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            logger?.LogDebug("NullQuotaManager: Getting quotas for user {UserId}", userId);
            return Task.FromResult(Enumerable.Empty<QuotaStatus>());
        }

        public Task ResetExpiredQuotasAsync(CancellationToken cancellationToken = default)
        {
            logger?.LogDebug("NullQuotaManager: Resetting expired quotas");
            return Task.CompletedTask;
        }
    }
}