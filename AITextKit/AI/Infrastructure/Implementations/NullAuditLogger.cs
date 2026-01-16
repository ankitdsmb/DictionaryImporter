namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations
{
    public class NullAuditLogger(ILogger<NullAuditLogger> logger = null) : IAuditLogger
    {
        public Task LogRequestAsync(AuditLogEntry entry)
        {
            logger?.LogDebug("NullAuditLogger: Logging request {RequestId}", entry.RequestId);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<AuditLogEntry>> GetRecentRequestsAsync(
            string providerName = null,
            string userId = null,
            int limit = 100)
        {
            return Task.FromResult(Enumerable.Empty<AuditLogEntry>());
        }

        public Task<IEnumerable<AuditSummary>> GetAuditSummaryAsync(DateTime from, DateTime to)
        {
            return Task.FromResult(Enumerable.Empty<AuditSummary>());
        }
    }
}