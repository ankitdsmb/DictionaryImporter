namespace DictionaryImporter.Infrastructure.Grammar;

public class GrammarSettings
{
    public string LanguageToolUrl { get; set; } = "http://localhost:2026";
    public string HunspellDictionaryPath { get; set; } = "Dictionaries";
    public string PatternRulesPath { get; set; } = "grammar-rules.json";
    public string NTextCatProfilePath { get; set; } = "Core14.profile.xml";
    public string GrammarNetApiKey { get; set; } = string.Empty;
    public int MinDefinitionLength { get; set; } = 20;
    public string DefaultLanguage { get; set; } = "en-US";
    public bool EnableAdvancedFeatures { get; set; } = true;
}