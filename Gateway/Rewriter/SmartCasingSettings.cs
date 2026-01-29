namespace DictionaryImporter.Gateway.Rewriter;

public sealed class SmartCasingSettings
{
    public double UppercaseRatioThreshold { get; set; } = 0.8;
    public int MinWordCountForShouting { get; set; } = 2;
    public bool PreserveMixedCaseBrands { get; set; } = true;
    public bool HandleHyphenatedWords { get; set; } = true;
    public List<string> MixedCaseBrands { get; set; } = new();
}