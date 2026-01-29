namespace DictionaryImporter.Core.Abstractions;

public sealed class ExampleRewriteEnhancement
{
    public long DictionaryEntryExampleId { get; set; }
    public string OriginalExampleText { get; set; } = string.Empty;
    public string RewrittenExampleText { get; set; } = string.Empty;

    public string Model { get; set; } = "Regex+RewriteMap+Humanizer";
    public int Confidence { get; set; } = 100;
}