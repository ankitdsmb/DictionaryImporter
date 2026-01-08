namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergRawEntry
    {
        public string Headword { get; init; } = null!;

        public List<string> Lines { get; init; } = new();
    }
}
