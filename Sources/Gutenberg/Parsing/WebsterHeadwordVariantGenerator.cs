namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterHeadwordVariantGenerator
    {
        public static IReadOnlyList<(string Variant, string Type)>
            Generate(string headword)
        {
            var results =
                new List<(string Variant, string Type)>();

            if (string.IsNullOrWhiteSpace(headword))
                return results;

            var original =
                headword.Trim();

            var lower =
                original.ToLowerInvariant();

            if (lower.Contains('-'))
            {
                var noHyphen =
                    lower.Replace("-", string.Empty);

                Add(results, noHyphen, "hyphen");
            }

            if (lower.Contains('\''))
            {
                var noApostrophe =
                    lower.Replace("'", string.Empty);

                Add(results, noApostrophe, "apostrophe");
            }

            if (lower.Contains(' '))
            {
                var collapsed =
                    Regex.Replace(lower, @"\s+", string.Empty);

                Add(results, collapsed, "spacing");
            }

            if (lower.Contains("ph"))
            {
                var modern =
                    lower.Replace("ph", "f");

                Add(results, modern, "archaic");
            }

            return results;
        }

        private static void Add(
            List<(string Variant, string Type)> list,
            string value,
            string type)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (value.Any(c => !char.IsLetter(c)))
                return;

            if (list.Any(v => v.Variant == value))
                return;

            list.Add((value, type));
        }
    }
}