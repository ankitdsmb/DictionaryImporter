namespace DictionaryImporter.AITextKit.AI.Infrastructure;

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