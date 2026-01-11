using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Domain.Models;

namespace DictionaryImporter.Sources.Collins.Parsing
{
    public sealed class CollinsDefinitionParser : IDictionaryDefinitionParser
    {
        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            var definition = entry.Definition;

            // Extract main definition (before any 【 markers)
            var mainDefinition = CollinsParserHelper.ExtractMainDefinition(definition);

            // Extract examples
            var examples = CollinsParserHelper.ExtractExamples(definition).ToList();

            // Extract domain/grammar info
            var domain = CollinsParserHelper.ExtractDomain(definition);
            var grammar = CollinsParserHelper.ExtractGrammar(definition);

            // Build cross-references from examples (e.g., "See also:")
            var crossRefs = CollinsParserHelper.ExtractCrossReferences(definition);

            var parsedDefinition = new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = mainDefinition,
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber,
                Domain = domain,
                UsageLabel = grammar,
                CrossReferences = crossRefs,
                Synonyms = CollinsParserHelper.ExtractSynonymsFromExamples(examples)
            };

            yield return parsedDefinition;
        }
    }
}