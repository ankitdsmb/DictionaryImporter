namespace DictionaryImporter.Orchestration;

public sealed class ConcurrencyMetrics
{
    public int AvailableSlots { get; set; }
    public int MaxConcurrency { get; set; }
    public int ActiveSources { get; set; }
    public Dictionary<string, int> SourceConcurrency { get; set; } = new();
    public Dictionary<string, long> SourceDurations { get; set; } = new();
    public int QueueLength { get; set; }
    public ParallelProcessingSettings Settings { get; set; } = ParallelProcessingSettings.Default;
}