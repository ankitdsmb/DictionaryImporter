namespace DictionaryImporter.Infrastructure.Grammar;

public sealed class GrammarCorrectionSetting
{
    public bool Enabled { get; set; } = true;
    public int MinDefinitionLength { get; set; } = 20;
    public Dictionary<string, bool> SourceSettings { get; set; } = new();
    public Dictionary<string, string> LanguageMappings { get; set; } = new();
    public string LanguageToolUrl { get; internal set; }

    public bool EnabledForSource(string sourceCode)
    {
        return Enabled &&
               (!SourceSettings.TryGetValue(sourceCode, out var enabled) || enabled);
    }

    public string GetLanguageCode(string sourceCode)
    {
        return LanguageMappings.TryGetValue(sourceCode, out var lang)
            ? lang
            : "en-US";
    }
}