using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Domain.Models;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.EnglishChinese.Parsing
{
    public sealed class EnglishChineseDefinitionParser
        : IDictionaryDefinitionParser
    {
        // IPA: /ɪˈlɪzɪən/
        private static readonly Regex IpaRegex =
            new Regex(@"/[^/]+/",
                RegexOptions.Compiled);

        // English syllabification ONLY (no accents)
        // Matches: E·ly·si·an, de·gree
        // Does NOT match: dé·jà
        private static readonly Regex EnglishSyllableRegex =
            new Regex(
                @"^\s*[A-Za-z]+(?:·[A-Za-z]+)+\s*",
                RegexOptions.Compiled);

        // POS at start
        private static readonly Regex PosRegex =
            new Regex(
                @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // POS-only garbage
        private static readonly Regex PosOnlyRegex =
            new Regex(
                @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public IEnumerable<ParsedDefinition> Parse(
            DictionaryEntry entry)
        {
            var working = entry.Definition;

            if (string.IsNullOrWhiteSpace(working))
                yield break;

            // 1. Remove IPA
            working = IpaRegex.Replace(working, string.Empty);

            // 2. Remove English syllabified headword
            working = EnglishSyllableRegex.Replace(working, string.Empty);

            // 3. Remove leading POS
            working = PosRegex.Replace(working, string.Empty);

            // 4. Remove headword repetition (abbreviations, symbols)
            if (!string.IsNullOrWhiteSpace(entry.Word))
            {
                var hw = Regex.Escape(entry.Word);
                working = Regex.Replace(
                    working,
                    @"^\s*" + hw + @"\s+",
                    string.Empty,
                    RegexOptions.IgnoreCase);
            }

            // 5. Defensive: remove any stray arrows
            working = working.Replace("⬄", "");

            // 6. Final cleanup
            working = Regex.Replace(working, @"\s+", " ").Trim();

            // 7. Drop POS-only junk
            if (string.IsNullOrWhiteSpace(working) || PosOnlyRegex.IsMatch(working))
            {
                yield return new ParsedDefinition
                {
                    Definition = null,              // explicitly empty
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