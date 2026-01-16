namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

internal class ProviderPerformanceSummary
{
    public string ProviderName { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public long TotalTokens { get; set; }
    public decimal TotalCost { get; set; }
    public long TotalDurationMs { get; set; }
}