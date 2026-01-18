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
            if (string.IsNullOrWhiteSpace(entry.RawFragment))
            {
                yield return new ParsedDefinition
                {
                    MeaningTitle = entry.Word ?? "unnamed sense",
                    Definition = entry.Definition ?? string.Empty,
                    RawFragment = entry.Definition ?? string.Empty,
                    SenseNumber = entry.SenseNumber,
                    Domain = null,
                    UsageLabel = null,
                    CrossReferences = new List<CrossReference>(),
                    Synonyms = null,
                    Alias = null
                };
                yield break;
            }

            // Do parsing work first
            bool shouldSkip = false;
            List<string> definitions = new List<string>();
            List<ParsedDefinition> parsedDefinitions = new List<ParsedDefinition>();

            try
            {
                // Skip non-English entries
                if (!IsEnglishEntry(entry.RawFragment))
                {
                    _logger.LogDebug("Skipping non-English Kaikki entry: {Word}", entry.Word);
                    shouldSkip = true;
                }
                else
                {
                    definitions = ExtractEnglishDefinitions(entry.RawFragment);

                    if (definitions.Count == 0)
                    {
                        _logger.LogDebug("No English definitions found for Kaikki entry: {Word}", entry.Word);
                        shouldSkip = true;
                    }
                    else
                    {
                        var senseNumber = 1;
                        foreach (var definition in definitions)
                        {
                            parsedDefinitions.Add(new ParsedDefinition
                            {
                                MeaningTitle = entry.Word ?? "unnamed sense",
                                Definition = definition,
                                RawFragment = entry.RawFragment,
                                SenseNumber = senseNumber++,
                                Domain = ExtractDomain(entry.RawFragment),
                                UsageLabel = ExtractUsageLabel(entry.RawFragment),
                                CrossReferences = ExtractCrossReferences(entry.RawFragment),
                                Synonyms = ExtractSynonymsList(entry.RawFragment),
                                Alias = null
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Kaikki entry: {Word}", entry.Word);

                // Fallback
                parsedDefinitions.Add(new ParsedDefinition
                {
                    MeaningTitle = entry.Word ?? "unnamed sense",
                    Definition = entry.Definition ?? string.Empty,
                    RawFragment = entry.RawFragment,
                    SenseNumber = entry.SenseNumber,
                    Domain = null,
                    UsageLabel = null,
                    CrossReferences = new List<CrossReference>(),
                    Synonyms = null,
                    Alias = null
                });
            }

            // Now yield results
            if (shouldSkip && parsedDefinitions.Count == 0)
                yield break;

            foreach (var parsedDef in parsedDefinitions)
            {
                yield return parsedDef;
            }
        }

        private bool IsEnglishEntry(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("lang_code", out var langCode) &&
                    langCode.ValueKind == JsonValueKind.String)
                {
                    return langCode.GetString() == "en";
                }

                if (root.TryGetProperty("lang", out var lang) &&
                    lang.ValueKind == JsonValueKind.String)
                {
                    var langStr = lang.GetString();
                    return langStr == "English" ||
                           langStr?.Contains("english", StringComparison.OrdinalIgnoreCase) == true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private List<string> ExtractEnglishDefinitions(string json)
        {
            var definitions = new List<string>();

            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        // Check if this sense is English
                        if (!IsEnglishSense(sense))
                            continue;

                        if (sense.TryGetProperty("glosses", out var glosses) &&
                            glosses.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var gloss in glosses.EnumerateArray())
                            {
                                if (gloss.ValueKind == JsonValueKind.String)
                                {
                                    var definition = gloss.GetString();
                                    if (!string.IsNullOrWhiteSpace(definition))
                                    {
                                        definitions.Add(definition);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return definitions;
        }

        private bool IsEnglishSense(JsonElement sense)
        {
            if (sense.TryGetProperty("lang_code", out var langCode) &&
                langCode.ValueKind == JsonValueKind.String)
            {
                return langCode.GetString() == "en";
            }

            return true;
        }

        private string? ExtractDomain(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (sense.TryGetProperty("categories", out var categories) &&
                            categories.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var category in categories.EnumerateArray())
                            {
                                if (category.TryGetProperty("name", out var name) &&
                                    name.ValueKind == JsonValueKind.String)
                                {
                                    return name.GetString();
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return null;
        }

        private string? ExtractUsageLabel(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (sense.TryGetProperty("tags", out var tags) &&
                            tags.ValueKind == JsonValueKind.Array)
                        {
                            var tagList = new List<string>();
                            foreach (var tag in tags.EnumerateArray())
                            {
                                if (tag.ValueKind == JsonValueKind.String)
                                {
                                    tagList.Add(tag.GetString() ?? "");
                                }
                            }

                            if (tagList.Count > 0)
                                return string.Join(", ", tagList);
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return null;
        }

        private List<CrossReference> ExtractCrossReferences(string rawFragment)
        {
            var crossRefs = new List<CrossReference>();

            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (sense.TryGetProperty("related", out var related) &&
                            related.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var rel in related.EnumerateArray())
                            {
                                if (rel.TryGetProperty("word", out var word) &&
                                    word.ValueKind == JsonValueKind.String)
                                {
                                    var targetWord = word.GetString();
                                    var relationType = "related";

                                    if (rel.TryGetProperty("sense", out var senseText) &&
                                        senseText.ValueKind == JsonValueKind.String)
                                    {
                                        relationType = senseText.GetString() ?? "related";
                                    }

                                    if (!string.IsNullOrWhiteSpace(targetWord))
                                    {
                                        crossRefs.Add(new CrossReference
                                        {
                                            TargetWord = targetWord,
                                            ReferenceType = relationType
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return crossRefs;
        }

        private List<string>? ExtractSynonymsList(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                var synonyms = new List<string>();

                if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (sense.TryGetProperty("synonyms", out var synonymsArray) &&
                            synonymsArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var synonym in synonymsArray.EnumerateArray())
                            {
                                if (synonym.TryGetProperty("word", out var word) &&
                                    word.ValueKind == JsonValueKind.String)
                                {
                                    var synonymWord = word.GetString();
                                    if (!string.IsNullOrWhiteSpace(synonymWord))
                                    {
                                        synonyms.Add(synonymWord);
                                    }
                                }
                            }
                        }
                    }
                }

                return synonyms.Count > 0 ? synonyms : null;
            }
            catch
            {
                return null;
            }
        }
    }
}