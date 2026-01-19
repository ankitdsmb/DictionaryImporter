using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;

namespace DictionaryImporter.Sources.Collins.Parsing
{
    public sealed class CollinsDefinitionParser : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "ENG_COLLINS";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            // ✅ never return empty
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var definition = entry.Definition;

            var mainDefinition = CollinsSourceDataHelper.ExtractMainDefinition(definition);

            var examples =
                CollinsSourceDataHelper.ExtractExamples(definition).ToList();

            var domain =
                CollinsSourceDataHelper.ExtractDomain(definition);

            var grammar =
                CollinsSourceDataHelper.ExtractGrammar(definition);

            var crossRefs =
                CollinsSourceDataHelper.ExtractCrossReferences(definition);

            var parsedDefinition = new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = mainDefinition,
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber,
                Domain = domain,
                UsageLabel = grammar,

                // ✅ ensure concrete list
                CrossReferences = crossRefs?.ToList() ?? new List<CrossReference>(),

                Synonyms = CollinsSourceDataHelper.ExtractSynonymsFromExamples(examples),
                Alias = null
            };

            // ✅ attach examples if your ParsedDefinition supports it
            if (examples.Count > 0)
                parsedDefinition.Examples = examples;

            yield return parsedDefinition;
        }
    }
}