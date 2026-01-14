namespace DictionaryImporter.AI.Core.Models;

public class ProviderMetrics
{
    public string Name { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (SuccessfulRequests * 100.0) / TotalRequests : 0;
    public double AverageResponseTimeMs { get; set; }
    public DateTime LastUsed { get; set; }
    public bool IsHealthy { get; set; }
    public Dictionary<string, int> ErrorCounts { get; set; } = new();
}