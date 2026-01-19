using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    public sealed class GutenbergDefinitionParser(ILogger<GutenbergDefinitionParser> logger)
        : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "GUT_WEBSTER";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            ParsedDefinition result;

            try
            {
                result = Build(entry);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse Gutenberg content for entry: {Word}", entry.Word);
                result = SourceDataHelper.CreateFallbackParsedDefinition(entry);
            }

            yield return result;
        }

        private static ParsedDefinition Build(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.RawFragment))
                return SourceDataHelper.CreateFallbackParsedDefinition(entry);

            var cleaned = CleanGutenbergText(entry.RawFragment);

            if (string.IsNullOrWhiteSpace(cleaned))
                return SourceDataHelper.CreateFallbackParsedDefinition(entry);

            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = cleaned,
                RawFragment = entry.RawFragment,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        private static string CleanGutenbergText(string raw)
        {
            var text = raw;

            text = Regex.Replace(text, @"\*\*\*\s*START OF.*?\*\*\*", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"\*\*\*\s*END OF.*?\*\*\*", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (text.Length > 800)
                text = text[..800].Trim();

            return text;
        }
    }
}