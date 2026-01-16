namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class QuotaUsageResult
{
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public int TokensUsed { get; set; }
    public decimal CostUsed { get; set; }
    public bool Success { get; set; }
    public DateTime RecordedAt { get; set; }
}