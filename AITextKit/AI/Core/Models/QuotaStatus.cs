namespace DictionaryImporter.AITextKit.AI.Core.Models;

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

    public decimal UsagePercentage => RequestLimit > 0 ? RequestsUsed * 100m / RequestLimit : 0;

    public bool IsNearLimit => UsagePercentage > 80;
    public bool IsExhausted => RequestsUsed >= RequestLimit || TokensUsed >= TokenLimit;
}