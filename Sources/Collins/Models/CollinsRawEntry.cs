namespace DictionaryImporter.Sources.Collins.Models
{
    public sealed class CollinsRawEntry
    {
        public string Headword { get; set; } = null!;

        public List<string> RawIpaLines { get; set; } = [];

        public List<ParsedIpa> ParsedIpa { get; set; } = [];

        public List<CollinsSenseRaw> Senses { get; set; } = [];
        public string? Etymology { get; set; }
    }
}