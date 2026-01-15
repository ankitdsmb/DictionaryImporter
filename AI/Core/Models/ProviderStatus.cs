namespace DictionaryImporter.AI.Core.Models;

public class ProviderStatus
{
    public string Name { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsHealthy { get; set; }
    public string HealthStatus { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public DateTime LastUsed { get; set; }
    public double SuccessRate { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public int TotalRequests { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Capabilities { get; set; } = new();
}