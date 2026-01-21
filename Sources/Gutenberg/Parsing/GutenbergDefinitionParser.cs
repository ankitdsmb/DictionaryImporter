using DictionaryImporter.Sources.Common.Parsing;
using static DictionaryImporter.Sources.Gutenberg.GutenbergParsingHelper;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    public sealed class GutenbergDefinitionParser(ILogger<GutenbergDefinitionParser> logger = null)
        : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "GUT_WEBSTER";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            // Always return something
            if (string.IsNullOrWhiteSpace(entry.Definition))
                return new[] { CreateFallbackParsedDefinition(entry) };

            var results = new List<ParsedDefinition>();

            try
            {
                var definition = entry.Definition;
                var parsedData = ParseGutenbergEntry(definition);

                // Extract synonyms from the full definition
                var synonyms = ExtractSynonyms(definition);
                parsedData.Synonyms = synonyms; // Ensure synonyms are set


                // If no clean definitions were extracted, create fallback
                if (parsedData.CleanDefinitions.Count == 0)
                {
                    logger?.LogWarning("No definitions extracted from Gutenberg entry: {Word}", entry.Word);
                    results.Add(CreateFallbackParsedDefinition(entry));
                    return results;
                }

                // Generate parsed definitions for each sense
                for (int i = 0; i < parsedData.CleanDefinitions.Count; i++)
                {
                    var cleanDef = parsedData.CleanDefinitions[i];
                    var rawBlock = parsedData.RawDefinitions.Count > i ? parsedData.RawDefinitions[i] : null;

                    // Get proper sense number from raw block or sequential
                    var senseNumber = rawBlock?.SenseNumber ?? (i + 1);

                    // Ensure sense number is valid
                    if (senseNumber <= 0)
                        senseNumber = i + 1;

                    // Build definition WITHOUT duplicating usage labels
                    // NOTE: Use your existing method if already created.
                    var definitionText = BuildCleanDefinition(cleanDef, parsedData, rawBlock);

                    results.Add(new ParsedDefinition
                    {
                        MeaningTitle = GetMeaningTitle(parsedData, entry, cleanDef),
                        Definition = definitionText,
                        RawFragment = entry.Definition,
                        SenseNumber = senseNumber,
                        PartOfSpeech = ExtractParsedPartOfSpeech(parsedData, rawBlock, entry),
                        Etymology = parsedData.Etymology,
                        Domain = GetDomain(parsedData, rawBlock),
                        UsageLabel = GetUsageLabel(parsedData, rawBlock),
                        CrossReferences = parsedData.CrossReferences.ToList(),
                        Synonyms = parsedData.Synonyms.Count > 0 ? parsedData.Synonyms.ToList() : null,
                        Alias = parsedData.Variants.FirstOrDefault(),
                        Examples = parsedData.Examples.ToList()
                    });
                }

                return results.Count > 0 ? results : new[] { CreateFallbackParsedDefinition(entry) };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to parse Gutenberg entry: {Word}", entry.Word);
                return new[] { CreateFallbackParsedDefinition(entry) };
            }
        }
    }
}