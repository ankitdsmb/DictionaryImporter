namespace DictionaryImporter.Core.Linguistics
{
    public sealed class EnUsOrthographicRule : IOrthographicSyllableRule
    {
        public string LocaleCode => "en-US";

        public IReadOnlyList<string> Apply(
            IReadOnlyList<string> syllables,
            string word)
        {
            return syllables
                .Select(s => s.Replace("i·on", "ion"))
                .ToList();
        }
    }
}