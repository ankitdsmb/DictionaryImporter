namespace DictionaryImporter.Sources.Century21.Models;

public sealed class Century21RawEntry
{
    public string Headword { get; init; } = null!;
    public string? Phonetics { get; init; }
    public string? PartOfSpeech { get; init; }
    public string Definition { get; init; } = null!;
    public string? GrammarInfo { get; init; } // For plural forms like "abacues, abaci"
    public List<Country21Example> Examples { get; init; } = new();
    public List<Country21Variant> Variants { get; init; } = new();
    public List<Country21Idiom> Idioms { get; init; } = new();
}

public sealed class Country21Example
{
    public string English { get; init; } = null!;
    public string? Chinese { get; init; }
}

public sealed class Country21Variant
{
    public string? PartOfSpeech { get; init; }
    public string Definition { get; init; } = null!;
    public List<Country21Example> Examples { get; init; } = new();
}

public sealed class Country21Idiom
{
    public string Headword { get; init; } = null!;
    public string Definition { get; init; } = null!;
    public List<Country21Example> Examples { get; init; } = new();
}