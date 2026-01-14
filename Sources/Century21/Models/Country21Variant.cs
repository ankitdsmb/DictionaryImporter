namespace DictionaryImporter.Sources.Century21.Models;

public sealed class Country21Variant
{
    public string? PartOfSpeech { get; init; }
    public string Definition { get; init; } = null!;
    public List<Country21Example> Examples { get; init; } = [];
}