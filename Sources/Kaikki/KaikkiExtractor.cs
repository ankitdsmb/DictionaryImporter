namespace DictionaryImporter.Sources.Kaikki
{
    public sealed class KaikkiExtractor : IDataExtractor<KaikkiRawEntry>
    {
        private readonly ILogger<KaikkiExtractor> _logger;

        public KaikkiExtractor(ILogger<KaikkiExtractor> logger)
        {
            _logger = logger;
        }

        public async IAsyncEnumerable<KaikkiRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var reader = new StreamReader(stream);
            string? line;
            int lineNumber = 0;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                KaikkiRawEntry? entry = null;

                try
                {
                    entry = ParseJsonLine(line);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse Kaikki line {LineNumber}", lineNumber);
                }

                if (entry != null && IsValidEntry(entry))
                {
                    yield return entry;
                }
                else if (lineNumber % 10000 == 0)
                {
                    _logger.LogDebug("Skipped invalid Kaikki entry at line {LineNumber}", lineNumber);
                }
            }
        }

        private KaikkiRawEntry? ParseJsonLine(string jsonLine)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonLine);
                var root = doc.RootElement;

                // Check if the root has the required properties
                if (!root.TryGetProperty("word", out var wordProp) ||
                    wordProp.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var word = wordProp.GetString();
                if (string.IsNullOrWhiteSpace(word))
                {
                    return null;
                }

                var entry = new KaikkiRawEntry
                {
                    Word = word,
                    Pos = GetStringProperty(root, "pos"),
                    LangCode = GetStringProperty(root, "lang_code") ?? "en",
                    EtymologyText = GetStringProperty(root, "etymology_text"),
                    Sounds = ExtractSounds(root),
                    Senses = ExtractSenses(root),
                    Forms = ExtractForms(root)
                };

                return entry;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse JSON line");
                return null;
            }
        }

        private string? GetStringProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            return null;
        }

        private List<KaikkiSound> ExtractSounds(JsonElement root)
        {
            var sounds = new List<KaikkiSound>();

            if (!root.TryGetProperty("sounds", out var soundsProp) ||
                soundsProp.ValueKind != JsonValueKind.Array)
            {
                return sounds;
            }

            foreach (var sound in soundsProp.EnumerateArray())
            {
                var kaikkiSound = new KaikkiSound
                {
                    Ipa = GetStringProperty(sound, "ipa"),
                    Audio = GetStringProperty(sound, "audio"),
                    Tags = ExtractTags(sound, "tags")
                };
                sounds.Add(kaikkiSound);
            }

            return sounds;
        }

        private string? ExtractTags(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var tagsProp) ||
                tagsProp.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var tags = new List<string>();
            foreach (var tag in tagsProp.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String)
                {
                    var tagValue = tag.GetString();
                    if (!string.IsNullOrWhiteSpace(tagValue))
                    {
                        tags.Add(tagValue);
                    }
                }
            }

            return tags.Count > 0 ? string.Join(",", tags) : null;
        }

        // Update ExtractSenses method to include synonyms:
        private List<KaikkiSense> ExtractSenses(JsonElement root)
        {
            var senses = new List<KaikkiSense>();

            if (!root.TryGetProperty("senses", out var sensesProp) ||
                sensesProp.ValueKind != JsonValueKind.Array)
            {
                return senses;
            }

            foreach (var sense in sensesProp.EnumerateArray())
            {
                var kaikkiSense = new KaikkiSense
                {
                    Glosses = ExtractStringArray(sense, "glosses"),
                    Examples = ExtractExamples(sense),
                    Synonyms = ExtractSynonyms(sense), // Add this line
                    Categories = ExtractStringArray(sense, "categories"),
                    Topics = ExtractStringArray(sense, "topics")
                };

                senses.Add(kaikkiSense);
            }

            return senses;
        }

        private List<string> ExtractStringArray(JsonElement element, string propertyName)
        {
            var list = new List<string>();

            if (!element.TryGetProperty(propertyName, out var arrayProp) ||
                arrayProp.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var item in arrayProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        list.Add(value);
                    }
                }
            }

            return list;
        }

        private List<KaikkiExample> ExtractExamples(JsonElement senseElement)
        {
            var examples = new List<KaikkiExample>();

            if (!senseElement.TryGetProperty("examples", out var examplesProp) ||
                examplesProp.ValueKind != JsonValueKind.Array)
            {
                return examples;
            }

            foreach (var example in examplesProp.EnumerateArray())
            {
                var kaikkiExample = new KaikkiExample
                {
                    Text = GetStringProperty(example, "text")
                };
                examples.Add(kaikkiExample);
            }

            return examples;
        }

        private List<KaikkiForm> ExtractForms(JsonElement root)
        {
            var forms = new List<KaikkiForm>();

            if (!root.TryGetProperty("forms", out var formsProp) ||
                formsProp.ValueKind != JsonValueKind.Array)
            {
                return forms;
            }

            foreach (var form in formsProp.EnumerateArray())
            {
                var kaikkiForm = new KaikkiForm
                {
                    Form = GetStringProperty(form, "form"),
                    Tags = ExtractStringArray(form, "tags")
                };
                forms.Add(kaikkiForm);
            }

            return forms;
        }

        private bool IsValidEntry(KaikkiRawEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
                return false;

            if (entry.Senses.Count == 0)
                return false;

            // Ensure at least one sense has at least one gloss
            return entry.Senses.Any(s => s.Glosses.Count > 0);
        }

        private List<KaikkiSynonym> ExtractSynonyms(JsonElement element, string propertyName = "synonyms")
        {
            var synonyms = new List<KaikkiSynonym>();

            if (!element.TryGetProperty(propertyName, out var synonymsProp) ||
                synonymsProp.ValueKind != JsonValueKind.Array)
            {
                return synonyms;
            }

            foreach (var synonym in synonymsProp.EnumerateArray())
            {
                var kaikkiSynonym = new KaikkiSynonym
                {
                    Word = GetStringProperty(synonym, "word"),
                    Sense = GetStringProperty(synonym, "sense"),
                    Language = GetStringProperty(synonym, "language")
                };

                if (!string.IsNullOrWhiteSpace(kaikkiSynonym.Word))
                {
                    synonyms.Add(kaikkiSynonym);
                }
            }

            return synonyms;
        }

        // Also extract top-level synonyms
        private List<KaikkiSynonym> ExtractEntrySynonyms(JsonElement root)
        {
            return ExtractSynonyms(root, "synonyms");
        }
    }
}