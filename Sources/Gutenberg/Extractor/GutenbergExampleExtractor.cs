using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Gutenberg.Extractor
{
    public sealed class GutenbergExampleExtractor : IExampleExtractor
    {
        public string SourceCode => "GUT_WEBSTER";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            if (parsed == null)
                return examples;

            // ✅ IMPORTANT:
            // For Gutenberg, examples should be extracted from RawFragment (original source block),
            // not from parsed.Definition (which may already be cleaned or truncated).
            if (string.IsNullOrWhiteSpace(parsed.RawFragment))
                return examples;

            // ✅ Use helper only
            examples = ParsingHelperGutenberg.ExtractExamples(parsed.RawFragment);

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}