namespace DictionaryImporter.AI.Core.Models;

internal class ProviderHealth
{
    public string ProviderName { get; set; }
    public bool IsHealthy { get; set; } = true;
    public DateTime LastHealthCheck { get; set; }
    public int FailureCount { get; set; }
}