namespace DictionaryImporter.Sources.Kaikki.Models
{
    public sealed class KaikkiForm
    {
        public string Form { get; set; } = null!;

        public List<string> Tags { get; set; } = [];
    }
}