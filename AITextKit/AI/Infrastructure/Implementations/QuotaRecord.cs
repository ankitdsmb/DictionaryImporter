namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

internal class QuotaRecord
{
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public string PeriodType { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int RequestLimit { get; set; }
    public long TokenLimit { get; set; }
    public decimal? CostLimit { get; set; }
    public int RequestsUsed { get; set; }
    public long TokensUsed { get; set; }
    public decimal CostUsed { get; set; }
    public bool IsActive { get; set; }
}