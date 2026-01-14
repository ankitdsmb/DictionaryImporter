namespace DictionaryImporter.Core.Grammar.Configuration;

public sealed class EnhancedGrammarConfiguration
{
    public bool Enabled { get; set; } = false;
    public string PrimaryEngine { get; set; } = "LanguageTool";
    public BlendingStrategy BlendingStrategy { get; set; } = BlendingStrategy.ConfidenceWeighted;
    public string LanguageToolUrl { get; set; } = "http://localhost:2026";
    public string HunspellDictionaryPath { get; set; } = "Dictionaries";
    public string CustomRulesPath { get; set; } = "grammar-rules.json";
    public string NTextCatProfilePath { get; set; } = "Core14.profile.xml";
    public Dictionary<string, double> EngineWeights { get; set; } = new();
    public bool EnableTraining { get; set; } = true;
    public int MinimumConfidenceThreshold { get; set; } = 70;

    public GrammarPipelineConfiguration ToPipelineConfiguration()
    {
        return new GrammarPipelineConfiguration
        {
            PrimaryEngine = PrimaryEngine,
            BlendingStrategy = BlendingStrategy,
            CustomRulesPath = CustomRulesPath,
            EngineWeights = EngineWeights,
            MinimumConfidence = MinimumConfidenceThreshold
        };
    }
}