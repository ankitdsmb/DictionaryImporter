namespace DictionaryImporter.Core.Orchestration.Pipeline.Steps;

public sealed class RuleBasedRewriteExamplesOptions
{
    public bool Enabled { get; set; } = false;

    public int Take { get; set; } = 1000;

    public int ConfidenceScore { get; set; } = 100;

    public string Model { get; set; } = "Regex+RewriteMap+Humanizer";

    // ✅ NEW (added)
    public bool ForceRewrite { get; set; } = false;
}