using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using HtmlAgilityPack;

namespace DictionaryImporter.Sources.Century21.Parsing
{
    public sealed class Century21DefinitionParser(ILogger<Century21DefinitionParser> logger)
        : ISourceDictionaryDefinitionParser
    {
        private readonly ILogger<Century21DefinitionParser> _logger = logger;

        public string SourceCode => "CENTURY21";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            // ✅ Never return empty list
            if (string.IsNullOrWhiteSpace(entry.RawFragment))
            {
                yield return CreateFallback(entry);
                yield break;
            }

            var results = new List<ParsedDefinition>();

            try
            {
                // Use helper to parse HTML
                var parsedData = ParsingHelperCentury21.ParseCentury21Html(
                    entry.RawFragment, entry.Word);

                // Get domain and usage label
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(entry.RawFragment);
                var wordBlock = htmlDoc.DocumentNode.SelectSingleNode("//div[@class='word_block']");

                var domain = wordBlock != null
                    ? ParsingHelperCentury21.ExtractDomain(wordBlock)
                    : null;

                var usageLabel = wordBlock != null
                    ? ParsingHelperCentury21.ExtractUsageLabel(wordBlock)
                    : null;

                // Create main definition
                if (parsedData.Definitions.Any())
                {
                    var senseNumber = entry.SenseNumber;

                    foreach (var definition in parsedData.Definitions)
                    {
                        results.Add(CreateParsedDefinition(
                            parsedData.Headword,
                            definition,
                            parsedData.PartOfSpeech,
                            parsedData.IpaPronunciation,
                            usageLabel,
                            parsedData.Examples,
                            senseNumber++,
                            entry.RawFragment,
                            domain));
                    }
                }
                else
                {
                    // Still return something
                    results.Add(CreateFallback(entry));
                }

                // Create variant definitions
                var variantSenseNumber = entry.SenseNumber + parsedData.Definitions.Count();
                foreach (var variant in parsedData.Variants)
                {
                    if (variant.Definitions.Any())
                    {
                        foreach (var definition in variant.Definitions)
                        {
                            results.Add(CreateParsedDefinition(
                                parsedData.Headword,
                                definition,
                                variant.PartOfSpeech,
                                parsedData.IpaPronunciation,
                                variant.GrammarInfo,
                                variant.Examples,
                                variantSenseNumber++,
                                entry.RawFragment,
                                domain));
                        }
                    }
                }

                // Create idiom definitions
                foreach (var idiom in parsedData.Idioms)
                {
                    if (!string.IsNullOrWhiteSpace(idiom.Headword) &&
                        !string.IsNullOrWhiteSpace(idiom.Definition))
                    {
                        results.Add(new ParsedDefinition
                        {
                            MeaningTitle = idiom.Headword,
                            Definition = Helper.NormalizeDefinition(idiom.Definition),
                            RawFragment = $"Idiom: {idiom.Headword} - {idiom.Definition}",
                            SenseNumber = 1,
                            Domain = domain,
                            UsageLabel = "idiom",
                            CrossReferences = new List<CrossReference>(),
                            Synonyms = null,
                            Alias = null,
                            Examples = idiom.Examples.ToList()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Century21 HTML for entry: {Word}", entry.Word);
                results.Clear();
                results.Add(CreateFallback(entry));
            }

            foreach (var item in results)
                yield return item;
        }

        private ParsedDefinition CreateFallback(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = entry.Definition ?? string.Empty,
                RawFragment = entry.RawFragment ?? entry.Definition ?? string.Empty,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        private ParsedDefinition CreateParsedDefinition(
            string headword,
            string definition,
            string? partOfSpeech,
            string? ipaPronunciation,
            string? usageLabel,
            IReadOnlyList<string> examples,
            int senseNumber,
            string rawFragment,
            string? domain)
        {
            var parsed = new ParsedDefinition
            {
                MeaningTitle = headword,
                Definition = Helper.NormalizeDefinition(definition ?? string.Empty),
                RawFragment = rawFragment,
                SenseNumber = senseNumber,
                Domain = domain,
                UsageLabel = usageLabel,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };

            if (examples.Count > 0)
                parsed.Examples = examples.ToList();

            if (!string.IsNullOrWhiteSpace(ipaPronunciation))
                parsed.Definition = $"Pronunciation: {ipaPronunciation}\n" + parsed.Definition;

            if (!string.IsNullOrWhiteSpace(partOfSpeech) && partOfSpeech != "unk")
                parsed.Definition = $"({partOfSpeech}) " + parsed.Definition;

            return parsed;
        }
    }
}
