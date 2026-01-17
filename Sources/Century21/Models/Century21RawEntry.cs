namespace DictionaryImporter.Sources.Century21.Models
{
    public sealed class Century21RawEntry
    {
        public string Headword { get; init; } = null!;
        public string? Phonetics { get; init; }
        public string? PartOfSpeech { get; init; }
        public string Definition { get; init; } = null!;
        public string? GrammarInfo { get; init; }
        public List<Country21Example> Examples { get; init; } = [];
        public List<Country21Variant> Variants { get; init; } = [];
        public List<Country21Idiom> Idioms { get; init; } = [];
    }
}