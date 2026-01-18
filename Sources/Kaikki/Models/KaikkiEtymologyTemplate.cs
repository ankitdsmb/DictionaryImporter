namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiEtymologyTemplate
    {
        public string Name { get; set; } = null!;

        public Dictionary<string, string> Args { get; set; } = new();
    }
}