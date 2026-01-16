namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class QuotaUsage
{
    public int RequestsUsed { get; set; }
    public long TokensUsed { get; set; }
    public decimal CostUsed { get; set; }
}