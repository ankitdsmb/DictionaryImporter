namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class ProviderFailure
{
    public string Provider { get; set; } = string.Empty;
    public Exception Exception { get; set; }
    public DateTime FailureTime { get; set; }
    public string ErrorDetail { get; set; }
}