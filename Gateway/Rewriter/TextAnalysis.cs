namespace DictionaryImporter.Gateway.Rewriter;

internal sealed class TextAnalysis
{
    public int WordCount { get; set; }
    public double UppercaseRatio { get; set; }
    public bool IsAllLowercase { get; set; }
    public bool IsAllUppercase { get; set; }
    public bool HasMixedCase { get; set; }
    public bool NeedsProcessing { get; set; }
}