namespace DictionaryImporter.Sources.EnglishChinese.Parsing
{
    public sealed class EnglishChineseDefinitionParser
        : IDictionaryDefinitionParser
    {
        private static readonly Regex IpaRegex =
            new(@"/[^/]+/",
                RegexOptions.Compiled);

        private static readonly Regex EnglishSyllableRegex =
            new(
                @"^\s*[A-Za-z]+(?:·[A-Za-z]+)+\s*",
                RegexOptions.Compiled);

        private static readonly Regex PosRegex =
            new(
                @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PosOnlyRegex =
            new(
                @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public IEnumerable<ParsedDefinition> Parse(
            DictionaryEntry entry)
        {
            var working = entry.Definition;

            if (string.IsNullOrWhiteSpace(working))
                yield break;

            working = IpaRegex.Replace(working, string.Empty);

            working = EnglishSyllableRegex.Replace(working, string.Empty);

            working = PosRegex.Replace(working, string.Empty);

            if (!string.IsNullOrWhiteSpace(entry.Word))
            {
                var hw = Regex.Escape(entry.Word);
                working = Regex.Replace(
                    working,
                    @"^\s*" + hw + @"\s+",
                    string.Empty,
                    RegexOptions.IgnoreCase);
            }

            working = working.Replace("⬄", "");

            working = Regex.Replace(working, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(working) || PosOnlyRegex.IsMatch(working))
            {
                yield return new ParsedDefinition
                {
                    Definition = null,
                    RawFragment = entry.Definition,
                    SenseNumber = entry.SenseNumber
                };
                yield break;
            }

            yield return new ParsedDefinition
            {
                Definition = working,
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber
            };
        }
    }
}