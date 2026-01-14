namespace DictionaryImporter.Sources.Century21.Models;

public sealed class Country21Idiom
{
    public string Headword { get; init; } = null!;
    public string Definition { get; init; } = null!;
    public List<Country21Example> Examples { get; init; } = [];
}