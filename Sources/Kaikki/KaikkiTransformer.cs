namespace DictionaryImporter.Sources.Kaikki
{
    public sealed class KaikkiTransformer : IDataTransformer<KaikkiRawEntry>
    {
        private readonly ILogger<KaikkiTransformer> _logger;

        public KaikkiTransformer(ILogger<KaikkiTransformer> logger)
        {
            _logger = logger;
        }

        public IEnumerable<DictionaryEntry> Transform(KaikkiRawEntry raw)
        {
            if (raw == null || string.IsNullOrWhiteSpace(raw.RawJson))
                yield break;

            // We'll process the entry and yield results as we go
            // But we need to handle errors without using return in yield context

            bool shouldSkip = false;
            string? word = null;
            List<string> definitions = new List<string>();
            string? pos = null;

            // Do the parsing work first (outside of yield context)
            try
            {
                // Skip non-English entries
                if (!IsEnglishEntry(raw.RawJson))
                {
                    _logger.LogDebug("Skipping non-English Kaikki entry");
                    shouldSkip = true;
                }
                else
                {
                    var doc = JsonDocument.Parse(raw.RawJson);
                    var root = doc.RootElement;

                    // Extract word
                    word = root.TryGetProperty("word", out var w) && w.ValueKind == JsonValueKind.String
                        ? w.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(word))
                    {
                        _logger.LogDebug("Kaikki entry has no word");
                        shouldSkip = true;
                    }
                    else
                    {
                        // Extract definitions
                        definitions = ExtractEnglishDefinitions(root);

                        if (definitions.Count == 0)
                        {
                            _logger.LogDebug("No English definitions found for Kaikki entry: {Word}", word);
                            shouldSkip = true;
                        }
                        else
                        {
                            // Extract part of speech
                            pos = ExtractPartOfSpeech(root);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transform Kaikki entry");
                shouldSkip = true;
            }

            // Now yield results if we have valid data
            if (shouldSkip || string.IsNullOrWhiteSpace(word) || definitions.Count == 0)
                yield break;

            var senseNumber = 1;
            foreach (var definition in definitions)
            {
                yield return new DictionaryEntry
                {
                    Word = word,
                    NormalizedWord = NormalizeWord(word),
                    PartOfSpeech = pos,
                    Definition = definition,
                    SenseNumber = senseNumber++,
                    SourceCode = "KAIKKI",
                    CreatedUtc = DateTime.UtcNow,
                    RawFragment = raw.RawJson
                };
            }
        }

        private bool IsEnglishEntry(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check language code
                if (root.TryGetProperty("lang_code", out var langCode) &&
                    langCode.ValueKind == JsonValueKind.String)
                {
                    return langCode.GetString() == "en";
                }

                // Check lang property
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

        private List<string> ExtractEnglishDefinitions(JsonElement root)
        {
            var definitions = new List<string>();

            if (root.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array)
            {
                foreach (var sense in senses.EnumerateArray())
                {
                    // Check if this sense is English
                    if (!IsEnglishSense(sense))
                        continue;

                    // Try glosses first
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

            return definitions;
        }

        private bool IsEnglishSense(JsonElement sense)
        {
            if (sense.TryGetProperty("lang_code", out var langCode) &&
                langCode.ValueKind == JsonValueKind.String)
            {
                return langCode.GetString() == "en";
            }

            // Default to English if no language specified
            return true;
        }

        private string? ExtractPartOfSpeech(JsonElement root)
        {
            if (root.TryGetProperty("pos", out var pos) && pos.ValueKind == JsonValueKind.String)
            {
                var posStr = pos.GetString();
                return NormalizePartOfSpeech(posStr);
            }

            return null;
        }

        private string NormalizePartOfSpeech(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos))
                return "unk";

            var normalized = pos.Trim().ToLowerInvariant();

            return normalized switch
            {
                "noun" => "noun",
                "verb" => "verb",
                "adjective" => "adj",
                "adj" => "adj",
                "adverb" => "adv",
                "adv" => "adv",
                "preposition" => "preposition",
                "pronoun" => "pronoun",
                "conjunction" => "conjunction",
                "interjection" => "exclamation",
                "determiner" => "determiner",
                "numeral" => "numeral",
                "article" => "determiner",
                "particle" => "particle",
                "phrase" => "phrase",
                "prefix" => "prefix",
                "suffix" => "suffix",
                "abbreviation" => "abbreviation",
                "symbol" => "symbol",
                _ => "unk"
            };
        }

        private string NormalizeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            return word.ToLowerInvariant()
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();
        }
    }
}