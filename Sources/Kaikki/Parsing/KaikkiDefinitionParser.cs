namespace DictionaryImporter.Sources.Kaikki.Parsing
{
    public sealed class KaikkiDefinitionParser : IDictionaryDefinitionParser
    {
        private readonly ILogger<KaikkiDefinitionParser> _logger;

        public KaikkiDefinitionParser(ILogger<KaikkiDefinitionParser> logger)
        {
            _logger = logger;
        }

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                return new List<ParsedDefinition>
                {
                    new ParsedDefinition
                    {
                        MeaningTitle = entry.Word ?? "unnamed sense",
                        Definition = string.Empty,
                        RawFragment = entry.Definition ?? string.Empty,
                        SenseNumber = entry.SenseNumber
                    }
                };
            }

            try
            {
                var cleanDefinition = ExtractMainDefinition(entry.Definition);

                return new List<ParsedDefinition>
                {
                    new ParsedDefinition
                    {
                        MeaningTitle = entry.Word ?? "unnamed sense",
                        Definition = cleanDefinition,
                        RawFragment = entry.Definition,
                        SenseNumber = entry.SenseNumber,
                        Domain = ExtractDomain(entry.Definition),
                        UsageLabel = ExtractUsageLabel(entry.Definition)
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Kaikki definition for entry: {Word}", entry.Word);
                return new List<ParsedDefinition>
                {
                    new ParsedDefinition
                    {
                        MeaningTitle = entry.Word ?? "unnamed sense",
                        Definition = entry.Definition ?? string.Empty,
                        RawFragment = entry.Definition ?? string.Empty,
                        SenseNumber = entry.SenseNumber
                    }
                };
            }
        }

        private string ExtractMainDefinition(string definition)
        {
            // Kaikki definitions are formatted with markers
            var lines = definition.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            // Find the main definition line (not starting with markers)
            var mainDefinition = lines.FirstOrDefault(line =>
                !line.StartsWith("【Pronunciation】") &&
                !line.StartsWith("【POS】") &&
                !line.StartsWith("【Examples】") &&
                !line.StartsWith("【Etymology】") &&
                !line.StartsWith("【Domain】") &&
                !line.StartsWith("• "));

            return mainDefinition ?? definition;
        }

        private string? ExtractDomain(string definition)
        {
            var domainMatch = Regex.Match(definition, @"【Domain】(.+)");
            if (domainMatch.Success)
            {
                return domainMatch.Groups[1].Value.Trim();
            }
            return null;
        }

        private string? ExtractUsageLabel(string definition)
        {
            // Extract from POS or other markers
            var posMatch = Regex.Match(definition, @"【POS】(.+)");
            if (posMatch.Success)
            {
                return posMatch.Groups[1].Value.Trim();
            }
            return null;
        }
    }
}