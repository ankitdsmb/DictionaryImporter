namespace DictionaryImporter.Sources.Gutenberg.Extractor
{
    public sealed class WebsterExampleExtractor : IExampleExtractor
    {
        public string SourceCode => "GUT_WEBSTER";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(parsed.Definition))
                return examples;

            var quotedMatches = Regex.Matches(parsed.Definition, @"[""']([^""']+)[""']");
            foreach (Match match in quotedMatches) examples.Add(match.Groups[1].Value);

            var egMatches = Regex.Matches(
                parsed.Definition,
                @"(?:e\.g\.|for example|ex\.|example:)\s*([^.;]+)",
                RegexOptions.IgnoreCase);

            foreach (Match match in egMatches) examples.Add(match.Groups[1].Value.Trim());

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();
        }
    }
}