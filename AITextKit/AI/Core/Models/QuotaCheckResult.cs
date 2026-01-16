namespace DictionaryImporter.AITextKit.AI.Core.Models;

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