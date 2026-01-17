namespace DictionaryImporter.Sources.EnglishChinese
{
    public sealed class EnglishChineseTransformer
        : IDataTransformer<EnglishChineseRawEntry>
    {
        public IEnumerable<DictionaryEntry> Transform(
            EnglishChineseRawEntry raw)
        {
            if (raw == null)
                throw new ArgumentNullException(nameof(raw));

            var idx = raw.RawLine.IndexOf('⬄');
            if (idx < 0 || idx == raw.RawLine.Length - 1)
                yield break;

            var rhs = raw.RawLine.Substring(idx + 1).Trim();

            if (rhs.Length == 0)
                yield break;

            yield return new DictionaryEntry
            {
                Word = raw.Headword,
                NormalizedWord = Normalize(raw.Headword),
                Definition = rhs,
                SenseNumber = 1,
                SourceCode = "ENG_CHN",
                CreatedUtc = DateTime.UtcNow
            };
        }

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var s = input.ToLowerInvariant();

            s = s.Replace("(", "")
                .Replace(")", "");

            s = s.Replace(",", " ");

            s = Regex.Replace(s, @"\s+", " ").Trim();

            return s;
        }
    }
}