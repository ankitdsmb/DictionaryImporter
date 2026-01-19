using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Oxford.Parsing
{
    public sealed class OxfordDefinitionParser : ISourceDictionaryDefinitionParser
    {
        private readonly ILogger<OxfordDefinitionParser> _logger;

        public OxfordDefinitionParser(ILogger<OxfordDefinitionParser> logger)
        {
            _logger = logger;
        }

        public string SourceCode => "ENG_OXFORD";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            // ✅ must always return exactly 1 parsed definition
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var definition = entry.Definition;

            var mainDefinition = SourceDataHelper.ExtractMainDefinition(definition);

            var examples = OxfordParsingHelper.ExtractExamples(definition).ToList();

            var crossRefs =
                OxfordParsingHelper.ExtractCrossReferences(definition)
                ?? new List<CrossReference>();

            yield return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = mainDefinition,
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber,
                Domain = SourceDataHelper.ExtractSection(definition, "【Label】"),
                UsageLabel = null,
                CrossReferences = crossRefs,
                Synonyms = ExtractSynonymsFromExamples(examples),
                Alias = SourceDataHelper.ExtractSection(definition, "【Variants】")
            };
        }

        // Strict: keep this inside Oxford parser to avoid changing synonym behavior across sources
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
            {
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
            }

            return synonyms.Count > 0 ? synonyms.ToList() : null;
        }
    }
}