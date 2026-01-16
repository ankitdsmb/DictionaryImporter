namespace DictionaryImporter.AITextKit.AI.Core.Models;

internal class ProviderHealth
{
    public string ProviderName { get; set; }
    public bool IsHealthy { get; set; } = true;
    public DateTime LastHealthCheck { get; set; }
    public int FailureCount { get; set; }
    public DateTime? LastFailure { get; set; }
    public string LastError { get; set; }
    public double SuccessRate { get; set; }
    public double ResponseTimeMs { get; set; }
}