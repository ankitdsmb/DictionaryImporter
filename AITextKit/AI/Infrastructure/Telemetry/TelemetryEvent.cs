namespace DictionaryImporter.AITextKit.AI.Infrastructure.Telemetry;

public class TelemetryEvent
{
    public TelemetryEventType EventType { get; set; }
    public string RequestId { get; set; }
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public RequestType? RequestType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Properties { get; set; } = new();
    public Dictionary<string, double> Metrics { get; set; } = new();
    public object Data { get; set; }
}