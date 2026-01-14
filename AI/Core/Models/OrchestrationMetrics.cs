namespace DictionaryImporter.AI.Core.Models;

public class OrchestrationMetrics
{
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (SuccessfulRequests * 100.0) / TotalRequests : 0;
    public double AverageResponseTimeMs { get; set; }
    public Dictionary<string, ProviderMetrics> ProviderMetrics { get; set; } = new();
    public Dictionary<RequestType, TypeMetrics> TypeMetrics { get; set; } = new();
}