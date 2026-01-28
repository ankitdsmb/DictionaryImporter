namespace DictionaryImporter.Gateway.Rewriter;

public sealed class TokenPreservationConfig
{
    public string Name { get; set; } = "Default Config";
    public string Version { get; set; } = "1.0.0";
    public TokenPreservationRules Rules { get; set; } = new();
    public SmartCasingSettings SmartSettings { get; set; } = new();
}