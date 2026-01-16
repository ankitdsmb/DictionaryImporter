namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

internal class AuditSummaryRecord
{
    public DateTime Date { get; set; }
    public string ProviderName { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public long TotalTokens { get; set; }
    public double AvgDurationMs { get; set; }
    public decimal? TotalCost { get; set; }
}