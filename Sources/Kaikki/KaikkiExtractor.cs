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
                    Forms = ExtractForms(root, "forms"),
                    Translations = ExtractTranslations(root),
                    HeadTemplates = ExtractHeadTemplates(root),
                    Hyphenations = ExtractHyphenations(root)
                };

                return entry;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse JSON line");
                return null;
            }
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
                    AudioUrl = ExtractAudioUrl(sound),
                    Tags = ExtractTags(sound, "tags"),
                    Rhymes = GetStringProperty(sound, "rhymes"),
                    Enpr = GetStringProperty(sound, "enpr")
                };
                sounds.Add(kaikkiSound);
            }

            return sounds;
        }

        private string? ExtractAudioUrl(JsonElement sound)
        {
            // Audio URL might be in different formats
            var audio = GetStringProperty(sound, "audio");
            var oggUrl = GetStringProperty(sound, "ogg_url");
            var mp3Url = GetStringProperty(sound, "mp3_url");

            return !string.IsNullOrWhiteSpace(mp3Url) ? mp3Url :
                   !string.IsNullOrWhiteSpace(oggUrl) ? oggUrl :
                   !string.IsNullOrWhiteSpace(audio) ? $"https:{audio}" : null;
        }

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
                    Synonyms = ExtractSynonyms(sense),
                    Antonyms = ExtractAntonyms(sense),
                    Related = ExtractRelated(sense),
                    Categories = ExtractStringArray(sense, "categories"),
                    Topics = ExtractStringArray(sense, "topics"),
                    Tags = ExtractStringArray(sense, "tags"),
                    Forms = ExtractForms(sense, "forms")
                };

                senses.Add(kaikkiSense);
            }

            return senses;
        }

        private List<KaikkiSynonym> ExtractSynonyms(JsonElement element)
        {
            var synonyms = new List<KaikkiSynonym>();

            if (!element.TryGetProperty("synonyms", out var synonymsProp) ||
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
                    Language = GetStringProperty(synonym, "language"),
                    Tags = ExtractTags(synonym, "tags")
                };

                if (!string.IsNullOrWhiteSpace(kaikkiSynonym.Word))
                {
                    synonyms.Add(kaikkiSynonym);
                }
            }

            return synonyms;
        }

        private List<KaikkiAntonym> ExtractAntonyms(JsonElement element)
        {
            var antonyms = new List<KaikkiAntonym>();

            if (!element.TryGetProperty("antonyms", out var antonymsProp) ||
                antonymsProp.ValueKind != JsonValueKind.Array)
            {
                return antonyms;
            }

            foreach (var antonym in antonymsProp.EnumerateArray())
            {
                var kaikkiAntonym = new KaikkiAntonym
                {
                    Word = GetStringProperty(antonym, "word"),
                    Sense = GetStringProperty(antonym, "sense")
                };

                if (!string.IsNullOrWhiteSpace(kaikkiAntonym.Word))
                {
                    antonyms.Add(kaikkiAntonym);
                }
            }

            return antonyms;
        }

        private List<KaikkiRelated> ExtractRelated(JsonElement element)
        {
            var related = new List<KaikkiRelated>();

            // Check for various related word fields
            var relatedTypes = new[] { "related", "see_also", "derived", "coordinate_terms" };

            foreach (var type in relatedTypes)
            {
                if (element.TryGetProperty(type, out var relatedProp) &&
                    relatedProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in relatedProp.EnumerateArray())
                    {
                        var kaikkiRelated = new KaikkiRelated
                        {
                            Word = GetStringProperty(item, "word") ??
                                  GetStringProperty(item, "text") ??
                                  (item.ValueKind == JsonValueKind.String ? item.GetString() : null),
                            Type = type.Replace("_", " "),
                            Sense = GetStringProperty(item, "sense")
                        };

                        if (!string.IsNullOrWhiteSpace(kaikkiRelated.Word))
                        {
                            related.Add(kaikkiRelated);
                        }
                    }
                }
            }

            return related;
        }

        private List<KaikkiExample> ExtractExamples(JsonElement sense)
        {
            var examples = new List<KaikkiExample>();

            if (!sense.TryGetProperty("examples", out var examplesProp) ||
                examplesProp.ValueKind != JsonValueKind.Array)
            {
                return examples;
            }

            foreach (var example in examplesProp.EnumerateArray())
            {
                var kaikkiExample = new KaikkiExample
                {
                    Text = GetStringProperty(example, "text"),
                    Translation = GetStringProperty(example, "translation"),
                    Language = GetStringProperty(example, "language")
                };
                examples.Add(kaikkiExample);
            }

            return examples;
        }

        private List<KaikkiTranslation> ExtractTranslations(JsonElement root)
        {
            var translations = new List<KaikkiTranslation>();

            if (!root.TryGetProperty("translations", out var transProp) ||
                transProp.ValueKind != JsonValueKind.Array)
            {
                return translations;
            }

            foreach (var translation in transProp.EnumerateArray())
            {
                var kaikkiTranslation = new KaikkiTranslation
                {
                    Language = GetStringProperty(translation, "lang"),
                    Code = GetStringProperty(translation, "code"),
                    Word = GetStringProperty(translation, "word"),
                    Sense = GetStringProperty(translation, "sense")
                };

                if (!string.IsNullOrWhiteSpace(kaikkiTranslation.Word))
                {
                    translations.Add(kaikkiTranslation);
                }
            }

            return translations;
        }

        private List<KaikkiHeadTemplate> ExtractHeadTemplates(JsonElement root)
        {
            var templates = new List<KaikkiHeadTemplate>();

            if (!root.TryGetProperty("head_templates", out var templatesProp) ||
                templatesProp.ValueKind != JsonValueKind.Array)
            {
                return templates;
            }

            foreach (var template in templatesProp.EnumerateArray())
            {
                var headTemplate = new KaikkiHeadTemplate
                {
                    Name = GetStringProperty(template, "name"),
                    Expansion = GetStringProperty(template, "expansion")
                };

                // Extract args
                if (template.TryGetProperty("args", out var argsProp) &&
                    argsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var arg in argsProp.EnumerateObject())
                    {
                        headTemplate.Args[arg.Name] = arg.Value.ToString();
                    }
                }

                templates.Add(headTemplate);
            }

            return templates;
        }

        private List<string> ExtractHyphenations(JsonElement root)
        {
            var hyphenations = new List<string>();

            if (!root.TryGetProperty("hyphenations", out var hyphenProp) ||
                hyphenProp.ValueKind != JsonValueKind.Array)
            {
                return hyphenations;
            }

            foreach (var hyphen in hyphenProp.EnumerateArray())
            {
                if (hyphen.TryGetProperty("parts", out var partsProp) &&
                    partsProp.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var part in partsProp.EnumerateArray())
                    {
                        if (part.ValueKind == JsonValueKind.String)
                        {
                            parts.Add(part.GetString()!);
                        }
                    }
                    if (parts.Count > 0)
                    {
                        hyphenations.Add(string.Join("‧", parts));
                    }
                }
            }

            return hyphenations;
        }

        // Helper methods (same as before but updated)
        private string? GetStringProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            return null;
        }

        private string? ExtractTags(JsonElement element, string propertyName)
        {
            var tags = ExtractStringArray(element, propertyName);
            return tags.Count > 0 ? string.Join(",", tags) : null;
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

        private List<KaikkiForm> ExtractForms(JsonElement element, string propertyName)
        {
            var forms = new List<KaikkiForm>();

            if (!element.TryGetProperty(propertyName, out var formsProp) ||
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

        private bool IsValidEntry(KaikkiRawEntry? entry)
        {
            if (entry == null)
                return false;

            if (string.IsNullOrWhiteSpace(entry.Word))
                return false;

            if (entry.Senses == null || entry.Senses.Count == 0)
                return false;

            return entry.Senses.Any(s => s != null &&
                                        s.Glosses != null &&
                                        s.Glosses.Count > 0 &&
                                        !string.IsNullOrWhiteSpace(s.Glosses[0]));
        }
    }
}