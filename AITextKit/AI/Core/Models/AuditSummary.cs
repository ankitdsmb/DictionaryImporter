namespace DictionaryImporter.AITextKit.AI.Core.Models;

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