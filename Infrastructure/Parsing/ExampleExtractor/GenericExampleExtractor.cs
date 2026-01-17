namespace DictionaryImporter.Infrastructure.Parsing.ExampleExtractor
{
    public sealed class GenericExampleExtractor : IExampleExtractor
    {
        public string SourceCode => "*";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(parsed.Definition))
                return examples;

            var quotedMatches = Regex.Matches(parsed.Definition, @"[""']([^""']+)[""']");
            foreach (Match match in quotedMatches)
                if (match.Groups[1].Value.Length > 10)
                    examples.Add(match.Groups[1].Value);

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();
        }
    }
}