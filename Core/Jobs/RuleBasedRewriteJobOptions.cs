namespace DictionaryImporter.Core.Jobs;

public sealed class RuleBasedRewriteJobOptions
{
    public bool Enabled { get; set; } = false;

    public int Take { get; set; } = 500;

    public string DefaultLanguageCode { get; set; } = "en-US";

    public string[] SourceCodes { get; set; } = [];
}