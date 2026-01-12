// DictionaryImporter/Sources/Oxford/Parsing/OxfordDefinitionParser.cs

using DictionaryImporter.Core.Parsing;

namespace DictionaryImporter.Sources.Gutenberg.Parsing;

public sealed class OxfordDefinitionParser : IDictionaryDefinitionParser
{
    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        var definition = entry.Definition;

        // Extract main definition (before any 【 markers)
        var mainDefinition = ExtractMainDefinition(definition);

        // Extract examples
        var examples = ExtractExamples(definition).ToList();

        // Extract pronunciation
        var pronunciation = ExtractPronunciation(definition);

        // Extract labels (domain/usage info)
        var label = ExtractLabel(definition);

        // Extract Chinese translation
        var chineseTranslation = ExtractChineseTranslation(definition);

        // Build cross-references
        var crossRefs = ExtractCrossReferences(definition);

        yield return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = mainDefinition,
            RawFragment = entry.Definition,
            SenseNumber = entry.SenseNumber,
            Domain = label,
            UsageLabel = null, // Oxford uses integrated labels
            CrossReferences = crossRefs,
            Synonyms = ExtractSynonymsFromExamples(examples),
            Alias = ExtractAlias(definition)
        };
    }

    private static string ExtractMainDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        // Find the first 【 marker
        var firstMarkerIndex = definition.IndexOf("【");
        if (firstMarkerIndex >= 0)
            return definition.Substring(0, firstMarkerIndex).Trim();

        return definition.Trim();
    }

    private static IReadOnlyList<string> ExtractExamples(string definition)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(definition))
            return examples;

        var lines = definition.Split('\n');
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
                    examples.Add(trimmed.Substring(1).Trim());
            }
        }

        return examples;
    }

    private static string? ExtractPronunciation(string definition)
    {
        return ExtractSection(definition, "【Pronunciation】");
    }

    private static string? ExtractLabel(string definition)
    {
        return ExtractSection(definition, "【Label】");
    }

    private static string? ExtractChineseTranslation(string definition)
    {
        return ExtractSection(definition, "【Chinese】");
    }

    private static string? ExtractAlias(string definition)
    {
        return ExtractSection(definition, "【Variants】");
    }

    private static string? ExtractSection(string definition, string marker)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        var startIndex = definition.IndexOf(marker);
        if (startIndex < 0)
            return null;

        startIndex += marker.Length;
        var endIndex = definition.IndexOf("【", startIndex);

        if (endIndex < 0)
            endIndex = definition.Length;

        return definition.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
    {
        var crossRefs = new List<CrossReference>();

        var seeAlsoSection = ExtractSection(definition, "【SeeAlso】");
        if (!string.IsNullOrEmpty(seeAlsoSection))
        {
            var references = seeAlsoSection.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var refWord in references)
            {
                var trimmed = refWord.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    crossRefs.Add(new CrossReference
                    {
                        TargetWord = trimmed,
                        ReferenceType = "SeeAlso"
                    });
            }
        }

        return crossRefs;
    }

    private static IReadOnlyList<string>? ExtractSynonymsFromExamples(IReadOnlyList<string> examples)
    {
        if (examples == null || examples.Count == 0)
            return null;

        var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var synonymPatterns = new[]
        {
            @"\b(?:synonymous|synonym|same as|equivalent to|also called)\s+(?:[\w\s]*?\s)?(?<word>\b[A-Z][a-z]+\b)",
            @"\b(?<word>\b[A-Z][a-z]+\b)\s*\((?:also|syn|syn\.|synonym)\)",
            @"\b(?<word1>\b[A-Z][a-z]+\b)\s+or\s+(?<word2>\b[A-Z][a-z]+\b)\b"
        };

        foreach (var example in examples)
        foreach (var pattern in synonymPatterns)
        {
            var matches = Regex.Matches(example, pattern);
            foreach (Match match in matches)
            {
                if (match.Groups["word"].Success)
                    synonyms.Add(match.Groups["word"].Value.ToLowerInvariant());

                if (match.Groups["word1"].Success)
                    synonyms.Add(match.Groups["word1"].Value.ToLowerInvariant());

                if (match.Groups["word2"].Success)
                    synonyms.Add(match.Groups["word2"].Value.ToLowerInvariant());
            }
        }

        return synonyms.Count > 0 ? synonyms.ToList() : null;
    }
}