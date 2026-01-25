namespace DictionaryImporter.Sources.Gutenberg.Models;

public sealed class GutenbergRawEntry
{
    public string Headword { get; set; } = null!;

    public List<string> Lines { get; init; } = [];
}