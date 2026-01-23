using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;

namespace DictionaryImporter.Sources.Oxford.Parsing
{
    public sealed class OxfordDefinitionParser(ILogger<OxfordDefinitionParser> logger)
        : ISourceDictionaryDefinitionParser
    {
        private readonly ILogger<OxfordDefinitionParser> _logger = logger;

        public string SourceCode => "ENG_OXFORD";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            // must always return exactly 1 parsed definition
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var definition = entry.Definition;

            // Parse all Oxford data at once
            var oxfordData = ParsingHelperOxford.ParseOxfordEntry(definition);
            var examples = ParsingHelperOxford.ExtractExamples(definition);
            var crossRefs = ParsingHelperOxford.ExtractCrossReferences(definition) ?? new List<CrossReference>();
            var synonyms = ParsingHelperOxford.ExtractSynonymsFromExamples(examples);
            // Build definition with IPA if available
            var fullDefinition = oxfordData.CleanDefinition;
            if (!string.IsNullOrWhiteSpace(oxfordData.IpaPronunciation))
            {
                fullDefinition = $"【Pronunciation】{oxfordData.IpaPronunciation}\n{fullDefinition}";
            }

            // Add variants if available
            if (oxfordData.Variants.Count > 0)
            {
                fullDefinition += $"\n【Variants】{string.Join(", ", oxfordData.Variants)}";
            }

            yield return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = fullDefinition,
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber,
                Domain = oxfordData.Domain,
                UsageLabel = oxfordData.UsageLabel ?? oxfordData.PartOfSpeech,
                CrossReferences = crossRefs,
                Synonyms = synonyms,
                Alias = oxfordData.Variants.FirstOrDefault()
            };
        }
    }
}