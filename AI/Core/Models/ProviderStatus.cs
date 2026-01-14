namespace DictionaryImporter.AI.Core.Models;

public class ProviderStatus
{
    public string Name { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsHealthy { get; set; }
    public string HealthStatus { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public List<string> Capabilities { get; set; } = new();
}