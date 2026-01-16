namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class CachedResponse
{
    public string CacheKey { get; set; }
    public string ProviderName { get; set; }
    public string Model { get; set; }
    public string ResponseText { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public int TokensUsed { get; set; }
    public int DurationMs { get; set; }
    public int HitCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}