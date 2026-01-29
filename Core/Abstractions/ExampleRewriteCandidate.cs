namespace DictionaryImporter.Core.Abstractions;

public sealed class ExampleRewriteCandidate
{
    public long DictionaryEntryExampleId { get; set; }
    public long DictionaryEntryParsedId { get; set; }
    public string ExampleText { get; set; } = string.Empty;
}