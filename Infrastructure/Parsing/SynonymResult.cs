namespace DictionaryImporter.Infrastructure.Parsing;

public class SynonymResult
{
    public string TargetHeadword { get; set; }
    public string ConfidenceLevel { get; set; } // "high", "medium", "low"
}