namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class TypeMetrics
{
    public RequestType Type { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public long TotalTokensUsed { get; set; }
    public decimal TotalCost { get; set; }

    public double SuccessRate => TotalRequests > 0 ? SuccessfulRequests * 100.0 / TotalRequests : 0;
}