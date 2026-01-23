namespace DictionaryImporter.Sources.Common.Helper;

public class Century21IdiomData
{
    public string Headword { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public IReadOnlyList<string> Examples { get; set; } = new List<string>();
}