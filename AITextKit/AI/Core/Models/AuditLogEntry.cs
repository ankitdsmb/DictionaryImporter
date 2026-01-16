namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class AuditLogEntry
{
    public Guid AuditId { get; set; } = Guid.NewGuid();
    public string RequestId { get; set; }
    public string ProviderName { get; set; }
    public string Model { get; set; }
    public string UserId { get; set; }
    public string SessionId { get; set; }
    public RequestType RequestType { get; set; }
    public string PromptHash { get; set; }
    public int PromptLength { get; set; }
    public int? ResponseLength { get; set; }
    public int TokensUsed { get; set; }
    public int DurationMs { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public string IpAddress { get; set; }
    public string UserAgent { get; set; }
    public Dictionary<string, object> RequestMetadata { get; set; } = new();
    public Dictionary<string, object> ResponseMetadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}