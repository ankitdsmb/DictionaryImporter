namespace DictionaryImporter.Sources.Collins.Models
{
    public sealed class CollinsRawEntry
    {
        public string Headword { get; set; } = null!;

        // Captured raw IPA lines
        public List<string> RawIpaLines { get; set; } = new();

        // NEW: parsed IPA results
        public List<ParsedIpa> ParsedIpa { get; set; } = new();

        public List<CollinsSenseRaw> Senses { get; set; } = new();
        public string? Etymology { get; set; }
    }

    public sealed class ParsedIpa
    {
        public string LocaleCode { get; set; } = null!;
        public string Ipa { get; set; } = null!;
    }
}
