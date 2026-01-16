namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

internal class ProviderPerformanceRecord
{
    public string ProviderName { get; set; }
    public DateTime MetricDate { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public long TotalTokens { get; set; }
    public decimal TotalCost { get; set; }
    public long TotalDurationMs { get; set; }
    public double SuccessRate { get; set; }
    public double AvgResponseTimeMs { get; set; }
}