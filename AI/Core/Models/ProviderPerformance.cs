namespace DictionaryImporter.AI.Core.Models;

public class ProviderPerformance
{
    public string Provider { get; set; } = string.Empty;
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, int> ErrorCounts { get; set; } = new();

    public double SuccessRate
    {
        get
        {
            return TotalRequests > 0 ? (SuccessfulRequests * 100.0) / TotalRequests : 0;
        }
    }
}