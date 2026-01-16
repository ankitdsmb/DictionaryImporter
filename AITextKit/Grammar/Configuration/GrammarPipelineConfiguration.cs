namespace DictionaryImporter.AITextKit.Grammar.Configuration;

public sealed class GrammarPipelineConfiguration
{
    public string PrimaryEngine { get; set; } = "LanguageTool";
    public BlendingStrategy BlendingStrategy { get; set; } = BlendingStrategy.ConfidenceWeighted;
    public string CustomRulesPath { get; set; } = "grammar-rules.json";
    public int MinimumConfidence { get; set; } = 70;
}