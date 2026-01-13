// File: DictionaryImporter.Infrastructure/Grammar/GrammarCorrectionSettings.cs
using System.Text.Json.Serialization;

namespace DictionaryImporter.Infrastructure.Grammar;

public sealed class GrammarCorrectionSettings
{
    public bool Enabled { get; set; } = true;
    public int MinDefinitionLength { get; set; } = 20;
    public string DefaultLanguage { get; set; } = "en-US";
    public string LanguageToolUrl { get; set; } = "http://localhost:2026";

    [JsonIgnore]
    public bool HasValidUrl => !string.IsNullOrWhiteSpace(LanguageToolUrl) &&
                               Uri.TryCreate(LanguageToolUrl, UriKind.Absolute, out _);

    public Dictionary<string, bool> SourceEnabled { get; set; } = new()
    {
        ["GUT_WEBSTER"] = true,
        ["ENG_COLLINS"] = true,
        ["ENG_OXFORD"] = true,
        ["ENG_CHN"] = true,
        ["STRUCT_JSON"] = true
    };

    public Dictionary<string, string> SourceLanguages { get; set; } = new()
    {
        ["GUT_WEBSTER"] = "en-US",
        ["ENG_COLLINS"] = "en-GB",
        ["ENG_OXFORD"] = "en-GB",
        ["ENG_CHN"] = "en-US",
        ["STRUCT_JSON"] = "en-US"
    };

    public bool EnabledForSource(string sourceCode)
    {
        if (!Enabled) return false;

        return !SourceEnabled.TryGetValue(sourceCode, out var enabled) || enabled;
    }

    public string GetLanguageCode(string sourceCode)
    {
        return SourceLanguages.TryGetValue(sourceCode, out var lang)
            ? lang
            : DefaultLanguage;
    }
}