namespace DictionaryImporter.Core.Orchestration.Models;

public sealed class ImportResult
{
    public string SourceCode { get; set; } = default!;
    public string SourceName { get; set; } = default!;
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public Exception? Exception { get; set; }
}