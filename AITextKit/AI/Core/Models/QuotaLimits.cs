namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class QuotaLimits
{
    public int RequestLimit { get; set; }
    public long TokenLimit { get; set; }
    public decimal? CostLimit { get; set; }
}