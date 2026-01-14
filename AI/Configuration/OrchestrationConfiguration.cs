namespace DictionaryImporter.AI.Configuration;

public class OrchestrationConfiguration
{
    public bool EnableFallback { get; set; } = true;

    public bool LogFailures { get; set; } = true;

    public int FailureHistorySize { get; set; } = 100;

    public int MaxConcurrentRequests { get; set; } = 10;
}