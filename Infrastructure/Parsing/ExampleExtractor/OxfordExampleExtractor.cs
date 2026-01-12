using DictionaryImporter.Core.Parsing;

namespace DictionaryImporter.Infrastructure.Parsing.ExampleExtractor;

public sealed class OxfordExampleExtractor : IExampleExtractor
{
    public string SourceCode => "ENG_OXFORD";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(parsed.RawFragment))
            return examples;

        var lines = parsed.RawFragment.Split('\n');
        var inExamplesSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("【Examples】"))
            {
                inExamplesSection = true;
                continue;
            }

            if (inExamplesSection)
            {
                if (trimmed.StartsWith("【") || string.IsNullOrEmpty(trimmed))
                    break;

                if (trimmed.StartsWith("»"))
                {
                    var example = trimmed.Substring(1).Trim();
                    if (!string.IsNullOrWhiteSpace(example))
                        examples.Add(example);
                }
            }
        }

        return examples
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct()
            .ToList();
    }
}