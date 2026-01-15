namespace DictionaryImporter.AI.Core.Models;

public class QuotaCheckResult
{
    public bool CanProceed { get; set; }
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public int RemainingRequests { get; set; }
    public long RemainingTokens { get; set; }
    public decimal RemainingCost { get; set; }
    public TimeSpan TimeUntilReset { get; set; }
    public bool IsNearLimit { get; set; }
    public QuotaLimits Limits { get; set; } = new QuotaLimits();
    public QuotaUsage CurrentUsage { get; set; } = new QuotaUsage();
}

public class QuotaUsageResult
{
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public int TokensUsed { get; set; }
    public decimal CostUsed { get; set; }
    public bool Success { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class QuotaStatus
{
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public string PeriodType { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int RequestLimit { get; set; }
    public int RequestsUsed { get; set; }
    public long TokenLimit { get; set; }
    public long TokensUsed { get; set; }
    public decimal? CostLimit { get; set; }
    public decimal CostUsed { get; set; }

    public decimal UsagePercentage => RequestLimit > 0 ? (RequestsUsed * 100m) / RequestLimit : 0;

    public bool IsNearLimit => UsagePercentage > 80;
    public bool IsExhausted => RequestsUsed >= RequestLimit || TokensUsed >= TokenLimit;
}

public class AuditLogEntry
{
    public Guid AuditId { get; set; } = Guid.NewGuid();
    public string RequestId { get; set; }
    public string ProviderName { get; set; }
    public string Model { get; set; }
    public string UserId { get; set; }
    public string SessionId { get; set; }
    public RequestType RequestType { get; set; }
    public string PromptHash { get; set; }
    public int PromptLength { get; set; }
    public int? ResponseLength { get; set; }
    public int TokensUsed { get; set; }
    public int DurationMs { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public Dictionary<string, object> RequestMetadata { get; set; } = new();
    public Dictionary<string, object> ResponseMetadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CachedResponse
{
    public string CacheKey { get; set; }
    public string ProviderName { get; set; }
    public string Model { get; set; }
    public string ResponseText { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public int TokensUsed { get; set; }
    public int DurationMs { get; set; }
    public int HitCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}