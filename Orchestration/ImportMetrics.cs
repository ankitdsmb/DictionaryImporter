namespace DictionaryImporter.Orchestration;

public sealed class ImportMetrics
{
    public int TotalSources { get; set; }
    public int CompletedSources { get; set; }
    public int SuccessfulSources { get; set; }
    public int FailedSources { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public PipelineMode Mode { get; set; }
}