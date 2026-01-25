using DictionaryImporter.Common;
using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Sources.StructuredJson.Parsing
{
    public sealed class StructuredJsonDefinitionParser(
        ILogger<StructuredJsonDefinitionParser> logger)
        : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "STRUCT_JSON";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            // ✅ Always return exactly 1 ParsedDefinition
            if (string.IsNullOrWhiteSpace(entry.RawFragment) &&
                string.IsNullOrWhiteSpace(entry.Definition))
            {
                return new List<ParsedDefinition>
                {
                    Helper.CreateFallbackParsedDefinition(entry)
                };
            }

            try
            {
                var raw = !string.IsNullOrWhiteSpace(entry.RawFragment)
                    ? entry.RawFragment
                    : entry.Definition;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return new List<ParsedDefinition>
                    {
                        Helper.CreateFallbackParsedDefinition(entry)
                    };
                }

                var cleaned = raw.Trim();

                return new List<ParsedDefinition>
                {
                    new ParsedDefinition
                    {
                        MeaningTitle = entry.Word ?? "unnamed sense",
                        Definition = cleaned,
                        RawFragment = raw,
                        SenseNumber = entry.SenseNumber,
                        Domain = null,
                        UsageLabel = null,
                        CrossReferences = new List<CrossReference>(),
                        Synonyms = null,
                        Alias = null
                    }
                };
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to parse StructuredJson content for entry: {Word}",
                    entry.Word);

                return new List<ParsedDefinition>
                {
                    Helper.CreateFallbackParsedDefinition(entry)
                };
            }
        }
    }
}