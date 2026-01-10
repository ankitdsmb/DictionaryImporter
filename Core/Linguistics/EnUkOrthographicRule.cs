namespace DictionaryImporter.Core.Linguistics
{
    public sealed class EnUkOrthographicRule : IOrthographicSyllableRule
    {
        public string LocaleCode => "en-UK";

        public IReadOnlyList<string> Apply(
            IReadOnlyList<string> syllables,
            string word)
        {
            var result = new List<string>();

            foreach (var s in syllables)
            {
                if (s.EndsWith("ion") && s.Length > 3)
                {
                    result.Add(s.Substring(0, s.Length - 3));
                    result.Add("ion");
                }
                else
                {
                    result.Add(s);
                }
            }

            return result.Where(x => x.Length > 0).ToList();
        }
    }
}