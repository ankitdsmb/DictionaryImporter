namespace DictionaryImporter.AITextKit.AI.Infrastructure;

public interface IAuditLogger
{
    Task LogRequestAsync(AuditLogEntry entry);

    Task<IEnumerable<AuditLogEntry>> GetRecentRequestsAsync(
        string providerName = null,
        string userId = null,
        int limit = 100);

    Task<IEnumerable<AuditSummary>> GetAuditSummaryAsync(DateTime from, DateTime to);
}