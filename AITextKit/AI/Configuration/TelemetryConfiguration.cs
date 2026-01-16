namespace DictionaryImporter.AITextKit.AI.Configuration;

public class TelemetryConfiguration
{
    public bool EnableTelemetry { get; set; } = true;
    public string Provider { get; set; } = "ApplicationInsights";
    public string ConnectionString { get; set; }
    public string InstrumentationKey { get; set; }
    public int MetricsExportIntervalSeconds { get; set; } = 30;
    public string LogLevel { get; set; } = "Information";
    public bool EnableRequestTracking { get; set; } = true;
    public bool EnableDependencyTracking { get; set; } = true;
    public bool EnablePerformanceCounters { get; set; } = true;
}