namespace DictionaryImporter.Core.Pipeline.Steps
{
    public sealed class RuleBasedRewriteDefinitionsOptions
    {
        public bool Enabled { get; set; } = true;

        public int Take { get; set; } = 500;

        public int MaxExamplesPerParsedDefinition { get; set; } = 10;

        public string Model { get; set; } = "Regex+RewriteMap+Humanizer";

        public string Provider { get; set; } = "RuleBased";

        // ✅ NEW (added)
        public bool ForceRewrite { get; set; } = false;
    }
}