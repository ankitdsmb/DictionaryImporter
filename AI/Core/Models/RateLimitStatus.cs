namespace DictionaryImporter.AI.Core.Models;

public class RateLimitStatus
{
    public int RemainingRequests { get; set; }
    public DateTime ResetTime { get; set; }
    public int Limit { get; set; }
    public TimeSpan TimeUntilReset => ResetTime - DateTime.UtcNow;
}