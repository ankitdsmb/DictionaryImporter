// EnglishChineseExampleExtractor.cs

using DictionaryImporter.Core.Parsing;

namespace DictionaryImporter.Infrastructure.Parsing.ExampleExtractor;

public sealed class EnglishChineseExampleExtractor : IExampleExtractor
{
    public string SourceCode => "ENG_CHN";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(parsed.Definition))
            return examples;

        // Chinese example markers
        var chineseMarkers = new[] { "例如", "比如", "例句", "例子" };

        foreach (var marker in chineseMarkers)
            if (parsed.Definition.Contains(marker))
            {
                var index = parsed.Definition.IndexOf(marker);
                if (index >= 0)
                {
                    var example = parsed.Definition.Substring(index + marker.Length).Trim();
                    var endIndex = example.IndexOfAny(new[] { '。', '.', ';', '，', ',' });
                    if (endIndex > 0) example = example.Substring(0, endIndex);
                    examples.Add(example.Trim());
                }
            }

        return examples
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct()
            .ToList();
    }
}