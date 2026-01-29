using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.Gutenberg.Extractor;

// Update the existing GutenbergExampleExtractor.cs
public sealed class GutenbergExampleExtractor : IExampleExtractor
{
    public string SourceCode => "GUT_WEBSTER";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        var examples = new List<string>();

        if (parsed == null)
            return examples;

        // ✅ IMPORTANT: Use RawFragment for extraction (maintains original format)
        if (string.IsNullOrWhiteSpace(parsed.RawFragment))
            return examples;

        // ✅ Use enhanced extraction from ParsingHelperGutenberg
        examples = ParsingHelperGutenberg.ExtractExamplesEnhanced(parsed.RawFragment);

        // ✅ ALSO extract from definition (for examples that might be embedded)
        if (!string.IsNullOrWhiteSpace(parsed.Definition))
        {
            var definitionExamples = ParsingHelperGutenberg.ExtractExamplesEnhanced(parsed.Definition);
            examples.AddRange(definitionExamples);
        }

        return examples
            .Select(e => e.NormalizeExample())
            .Where(e => e.IsValidExampleSentence())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}