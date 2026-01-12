namespace DictionaryImporter.Core.Linguistics;

public static class OrthographicSyllableRenderer
{
    public static string Render(
        IReadOnlyList<string> syllables)
    {
        return syllables == null || syllables.Count == 0
            ? string.Empty
            : string.Join("·", syllables);
    }
}