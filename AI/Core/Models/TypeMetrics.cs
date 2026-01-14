namespace DictionaryImporter.AI.Core.Models;

public class TypeMetrics
{
    public RequestType Type { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (SuccessfulRequests * 100.0) / TotalRequests : 0;
    public double AverageResponseTimeMs { get; set; }
}