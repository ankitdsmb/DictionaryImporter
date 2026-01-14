namespace DictionaryImporter.AI.Core.Models;

public class AiResponse
{
    public string Content { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long TokensUsed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }

    [JsonIgnore]
    public byte[] AudioData { get; set; }

    [JsonIgnore]
    public byte[] ImageData { get; set; }

    public string ImageFormat { get; set; }

    public string AudioFormat { get; set; }

    public decimal EstimatedCost { get; set; }

    public string Currency { get; set; } = "USD";

    public Dictionary<string, object> Metadata { get; set; } = new();

    public Dictionary<string, object> ProviderMetadata { get; set; } = new();

    public int RetryCount { get; set; }

    public List<string> AttemptedProviders { get; set; } = new();
}