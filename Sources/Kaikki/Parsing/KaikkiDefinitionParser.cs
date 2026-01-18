namespace DictionaryImporter.Sources.Kaikki.Parsing
{
    public sealed class KaikkiDefinitionParser : IDictionaryDefinitionParser
    {
        private readonly ILogger<KaikkiDefinitionParser> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public KaikkiDefinitionParser(ILogger<KaikkiDefinitionParser> logger)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
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
                // Try to extract structured data from RawFragment first
                if (!string.IsNullOrWhiteSpace(entry.RawFragment) &&
                    entry.RawFragment.StartsWith("{") && entry.RawFragment.EndsWith("}"))
                {
                    return ParseFromRawFragment(entry);
                }
                else
                {
                    return ParseFromFormattedDefinition(entry);
                }
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

        private IEnumerable<ParsedDefinition> ParseFromRawFragment(DictionaryEntry entry)
        {
            var rawData = JsonSerializer.Deserialize<KaikkiRawData>(entry.RawFragment!, _jsonOptions);
            if (rawData == null)
                return ParseFromFormattedDefinition(entry);

            var parsedDef = new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = rawData.Sense ?? ExtractMainDefinition(entry.Definition),
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber,
                Domain = ExtractDomainFromRawData(rawData),
                UsageLabel = rawData.Pos
            };

            // Extract cross-references from synonyms, antonyms, and related words
            parsedDef.CrossReferences = ExtractCrossReferences(rawData);

            // Extract synonyms
            parsedDef.Synonyms = ExtractSynonymsFromRawData(rawData);

            // Extract alias (alternative forms)
            parsedDef.Alias = ExtractAliasFromRawData(rawData);

            return new List<ParsedDefinition> { parsedDef };
        }

        private IEnumerable<ParsedDefinition> ParseFromFormattedDefinition(DictionaryEntry entry)
        {
            var cleanDefinition = ExtractMainDefinition(entry.Definition);
            var synonyms = ExtractSynonymsFromDefinition(entry.Definition);
            var crossRefs = ExtractCrossReferencesFromDefinition(entry.Definition);
            var examples = ExtractExamplesFromDefinition(entry.Definition);
            var etymology = ExtractEtymologyFromDefinition(entry.Definition);

            var parsedDef = new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = cleanDefinition,
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber,
                Domain = ExtractDomain(entry.Definition),
                UsageLabel = ExtractUsageLabel(entry.Definition),
                CrossReferences = crossRefs,
                Synonyms = synonyms.Count > 0 ? synonyms : null
            };

            // Add examples if found
            if (examples.Count > 0)
            {
                parsedDef.Examples = examples;
            }

            return new List<ParsedDefinition> { parsedDef };
        }

        private string ExtractMainDefinition(string definition)
        {
            var lines = definition.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            // Find the main definition line (not starting with markers)
            var mainDefinition = lines.FirstOrDefault(line =>
                !line.StartsWith("【Pronunciation】") &&
                !line.StartsWith("【POS】") &&
                !line.StartsWith("【Hyphenation】") &&
                !line.StartsWith("【Sense") &&
                !line.StartsWith("【Synonyms】") &&
                !line.StartsWith("【Antonyms】") &&
                !line.StartsWith("【Related】") &&
                !line.StartsWith("【Examples】") &&
                !line.StartsWith("【Forms】") &&
                !line.StartsWith("【Etymology】") &&
                !line.StartsWith("【Domain】") &&
                !line.StartsWith("【Tags】") &&
                !line.StartsWith("• "));

            return mainDefinition ?? definition;
        }

        private List<string> ExtractSynonymsFromDefinition(string definition)
        {
            var synonyms = new List<string>();

            if (!definition.Contains("【Synonyms】"))
                return synonyms;

            var lines = definition.Split('\n');
            var inSynonymsSection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("【Synonyms】"))
                {
                    inSynonymsSection = true;
                    continue;
                }

                if (inSynonymsSection)
                {
                    if (trimmedLine.StartsWith("【")) // New section started
                        break;

                    if (trimmedLine.StartsWith("• "))
                    {
                        var synonym = trimmedLine.Substring(2).Trim();

                        // Remove any parenthetical sense info
                        var parenIndex = synonym.IndexOf('(');
                        if (parenIndex > 0)
                            synonym = synonym.Substring(0, parenIndex).Trim();

                        if (!string.IsNullOrWhiteSpace(synonym))
                            synonyms.Add(synonym);
                    }
                }
            }

            return synonyms;
        }

        private List<CrossReference> ExtractCrossReferencesFromDefinition(string definition)
        {
            var crossRefs = new List<CrossReference>();

            // Extract from Related section
            if (definition.Contains("【Related】"))
            {
                var lines = definition.Split('\n');
                var inRelatedSection = false;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (trimmedLine.StartsWith("【Related】"))
                    {
                        inRelatedSection = true;
                        continue;
                    }

                    if (inRelatedSection)
                    {
                        if (trimmedLine.StartsWith("【")) // New section started
                            break;

                        if (trimmedLine.StartsWith("• "))
                        {
                            var related = trimmedLine.Substring(2).Trim();

                            // Extract word and type
                            var word = related;
                            var type = "Related";

                            var bracketIndex = related.IndexOf('[');
                            if (bracketIndex > 0)
                            {
                                word = related.Substring(0, bracketIndex).Trim();
                                type = related.Substring(bracketIndex + 1,
                                    related.IndexOf(']') - bracketIndex - 1).Trim();
                            }

                            if (!string.IsNullOrWhiteSpace(word))
                            {
                                crossRefs.Add(new CrossReference
                                {
                                    TargetWord = word,
                                    ReferenceType = type
                                });
                            }
                        }
                    }
                }
            }

            return crossRefs;
        }

        private List<CrossReference> ExtractCrossReferences(KaikkiRawData rawData)
        {
            var crossRefs = new List<CrossReference>();

            // Add related words
            if (rawData.Related != null)
            {
                foreach (var related in rawData.Related)
                {
                    if (!string.IsNullOrWhiteSpace(related.Word))
                    {
                        crossRefs.Add(new CrossReference
                        {
                            TargetWord = related.Word,
                            ReferenceType = related.Type ?? "Related"
                        });
                    }
                }
            }

            return crossRefs;
        }

        private List<string> ExtractSynonymsFromRawData(KaikkiRawData rawData)
        {
            var synonyms = new List<string>();

            if (rawData.Synonyms != null)
            {
                foreach (var synonym in rawData.Synonyms)
                {
                    if (!string.IsNullOrWhiteSpace(synonym.Word))
                    {
                        synonyms.Add(synonym.Word);
                    }
                }
            }

            return synonyms;
        }

        private string? ExtractAliasFromRawData(KaikkiRawData rawData)
        {
            if (rawData.Forms != null && rawData.Forms.Count > 0)
            {
                var forms = rawData.Forms
                    .Where(f => !string.IsNullOrWhiteSpace(f.Form))
                    .Select(f => f.Form!)
                    .Distinct()
                    .ToList();

                if (forms.Count > 0)
                {
                    return string.Join(", ", forms.Take(3));
                }
            }

            return null;
        }

        private List<string> ExtractExamplesFromDefinition(string definition)
        {
            var examples = new List<string>();

            if (!definition.Contains("【Examples】"))
                return examples;

            var lines = definition.Split('\n');
            var inExamplesSection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("【Examples】"))
                {
                    inExamplesSection = true;
                    continue;
                }

                if (inExamplesSection)
                {
                    if (trimmedLine.StartsWith("【")) // New section started
                        break;

                    if (trimmedLine.StartsWith("• "))
                    {
                        var example = trimmedLine.Substring(2).Trim();

                        // Remove translation part if present
                        var pipeIndex = example.IndexOf('|');
                        if (pipeIndex > 0)
                            example = example.Substring(0, pipeIndex).Trim();

                        if (!string.IsNullOrWhiteSpace(example))
                            examples.Add(example);
                    }
                }
            }

            return examples;
        }

        private string? ExtractEtymologyFromDefinition(string definition)
        {
            if (!definition.Contains("【Etymology】"))
                return null;

            var lines = definition.Split('\n');
            var inEtymologySection = false;
            var etymologyLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("【Etymology】"))
                {
                    inEtymologySection = true;
                    continue;
                }

                if (inEtymologySection)
                {
                    if (trimmedLine.StartsWith("【")) // New section started
                        break;

                    if (!trimmedLine.StartsWith("• "))
                    {
                        etymologyLines.Add(trimmedLine);
                    }
                }
            }

            return etymologyLines.Count > 0 ? string.Join(" ", etymologyLines) : null;
        }

        private string? ExtractDomain(string definition)
        {
            if (!definition.Contains("【Domain】"))
                return null;

            var domainMatch = Regex.Match(definition, @"【Domain】(.+?)(?=\n【|$)");
            if (domainMatch.Success)
            {
                return domainMatch.Groups[1].Value.Trim();
            }
            return null;
        }

        private string? ExtractDomainFromRawData(KaikkiRawData rawData)
        {
            var domainParts = new List<string>();

            if (rawData.Categories != null && rawData.Categories.Count > 0)
            {
                domainParts.Add($"Categories: {string.Join(", ", rawData.Categories.Take(2))}");
            }

            if (rawData.Topics != null && rawData.Topics.Count > 0)
            {
                domainParts.Add($"Topics: {string.Join(", ", rawData.Topics.Take(2))}");
            }

            return domainParts.Count > 0 ? string.Join("; ", domainParts) : null;
        }

        private string? ExtractUsageLabel(string definition)
        {
            var posMatch = Regex.Match(definition, @"【POS】(.+)");
            if (posMatch.Success)
            {
                return posMatch.Groups[1].Value.Trim();
            }
            return null;
        }

        // Helper classes for raw data deserialization
        private class KaikkiRawData
        {
            public string? Word { get; set; }
            public string? Pos { get; set; }
            public string? Sense { get; set; }
            public List<KaikkiRawSynonym>? Synonyms { get; set; }
            public List<KaikkiRawAntonym>? Antonyms { get; set; }
            public List<KaikkiRawRelated>? Related { get; set; }
            public List<KaikkiRawExample>? Examples { get; set; }
            public List<KaikkiRawForm>? Forms { get; set; }
            public List<string>? Categories { get; set; }
            public List<string>? Topics { get; set; }
            public List<string>? Tags { get; set; }
            public string? Etymology { get; set; }
            public List<KaikkiRawTranslation>? Translations { get; set; }
        }

        private class KaikkiRawSynonym
        {
            public string? Word { get; set; }
            public string? Sense { get; set; }
            public string? Language { get; set; }
        }

        private class KaikkiRawAntonym
        {
            public string? Word { get; set; }
            public string? Sense { get; set; }
        }

        private class KaikkiRawRelated
        {
            public string? Word { get; set; }
            public string? Type { get; set; }
            public string? Sense { get; set; }
        }

        private class KaikkiRawExample
        {
            public string? Text { get; set; }
            public string? Translation { get; set; }
            public string? Language { get; set; }
        }

        private class KaikkiRawForm
        {
            public string? Form { get; set; }
            public List<string>? Tags { get; set; }
        }

        private class KaikkiRawTranslation
        {
            public string? Language { get; set; }
            public string? Code { get; set; }
            public string? Word { get; set; }
            public string? Sense { get; set; }
        }
    }
}