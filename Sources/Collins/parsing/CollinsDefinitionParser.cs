namespace DictionaryImporter.Sources.Collins.parsing
{
    public sealed class CollinsDefinitionParser : IDictionaryDefinitionParser
    {
        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            var definition = entry.Definition;

            var mainDefinition = CollinsParserHelper.ExtractMainDefinition(definition);

            var examples = CollinsParserHelper.ExtractExamples(definition).ToList();

            var domain = CollinsParserHelper.ExtractDomain(definition);

            var grammar = CollinsParserHelper.ExtractGrammar(definition);

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