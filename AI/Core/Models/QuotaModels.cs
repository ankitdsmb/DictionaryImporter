namespace DictionaryImporter.AI.Core.Models;

public class QuotaUsage
{
    public int RequestsUsed { get; set; }
    public long TokensUsed { get; set; }
    public decimal CostUsed { get; set; }
}

public class QuotaLimits
{
    public int RequestLimit { get; set; }
    public long TokenLimit { get; set; }
    public decimal? CostLimit { get; set; }
}

public class ApiKeyInfo
{
    public string ProviderName { get; set; }
    public string KeyIdentifier { get; set; }
    public string KeyType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public string DeactivationReason { get; set; }
    public bool IsActive { get; set; }
}

public class AuditSummary
{
    public DateTime Date { get; set; }
    public string ProviderName { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public double SuccessRate { get; set; }
    public long TotalTokens { get; set; }
    public double AvgDurationMs { get; set; }
    public decimal TotalCost { get; set; }
}