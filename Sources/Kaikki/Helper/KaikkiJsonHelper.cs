// File: DictionaryImporter/Sources/Kaikki/Helpers/KaikkiJsonHelper.cs
namespace DictionaryImporter.Sources.Kaikki.Helpers
{
    internal static class KaikkiJsonHelper
    {
        public static bool IsEnglishEntry(string json)
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
                    return lang.GetString() == "English" ||
                           lang.GetString()?.ToLowerInvariant().Contains("english") == true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static List<string> ExtractEnglishDefinitions(string json)
        {
            var definitions = new List<string>();

            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!IsEnglishEntry(json))
                    return definitions;

                // Extract from senses array
                if (root.TryGetProperty("senses", out var senses) &&
                    senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        // Only English senses
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
                                    if (!string.IsNullOrWhiteSpace(definition) &&
                                        !IsTranslationList(definition))
                                    {
                                        definitions.Add(definition);
                                    }
                                }
                            }
                        }

                        // Fallback to raw_glosses
                        if (definitions.Count == 0 &&
                            sense.TryGetProperty("raw_glosses", out var rawGlosses) &&
                            rawGlosses.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var rawGloss in rawGlosses.EnumerateArray())
                            {
                                if (rawGloss.ValueKind == JsonValueKind.String)
                                {
                                    var definition = rawGloss.GetString();
                                    if (!string.IsNullOrWhiteSpace(definition) &&
                                        !IsTranslationList(definition))
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
                // Ignore parsing errors
            }

            return definitions;
        }

        private static bool IsEnglishSense(JsonElement sense)
        {
            // Check if this sense is for English language
            if (sense.TryGetProperty("lang_code", out var langCode) &&
                langCode.ValueKind == JsonValueKind.String)
            {
                return langCode.GetString() == "en";
            }

            // Default to English if no language specified
            return true;
        }

        private static bool IsTranslationList(string text)
        {
            // Check if this looks like a translation list (multilingual words)
            if (text.Contains("\"lang\":") || text.Contains("\"lang_code\":"))
                return true;

            if (text.Contains("Zulu") || text.Contains("Arabic") || text.Contains("Chinese"))
                return true;

            return false;
        }

        public static string? ExtractEtymology(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check etymology_text
                if (root.TryGetProperty("etymology_text", out var etymologyText) &&
                    etymologyText.ValueKind == JsonValueKind.String)
                {
                    return etymologyText.GetString();
                }

                // Check etymology_templates
                if (root.TryGetProperty("etymology_templates", out var templates) &&
                    templates.ValueKind == JsonValueKind.Array)
                {
                    var etymologyParts = new List<string>();

                    foreach (var template in templates.EnumerateArray())
                    {
                        if (template.TryGetProperty("args", out var args) &&
                            args.ValueKind == JsonValueKind.Object)
                        {
                            // Look for etymology-related arguments
                            foreach (var arg in args.EnumerateObject())
                            {
                                if (arg.Name.StartsWith("der") ||
                                    arg.Name.StartsWith("bor") ||
                                    arg.Name.StartsWith("inh") ||
                                    arg.Name.Contains("lang") ||
                                    arg.Name.Contains("etyl"))
                                {
                                    if (arg.Value.ValueKind == JsonValueKind.String)
                                    {
                                        var value = arg.Value.GetString();
                                        if (!string.IsNullOrWhiteSpace(value))
                                            etymologyParts.Add(value);
                                    }
                                }
                            }
                        }
                    }

                    if (etymologyParts.Count > 0)
                        return string.Join(" → ", etymologyParts);
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        public static List<string> ExtractExamples(string json)
        {
            var examples = new List<string>();

            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!IsEnglishEntry(json))
                    return examples;

                if (root.TryGetProperty("senses", out var senses) &&
                    senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (!IsEnglishSense(sense))
                            continue;

                        if (sense.TryGetProperty("examples", out var examplesArray) &&
                            examplesArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var example in examplesArray.EnumerateArray())
                            {
                                if (example.TryGetProperty("text", out var text) &&
                                    text.ValueKind == JsonValueKind.String)
                                {
                                    var exampleText = text.GetString();
                                    if (!string.IsNullOrWhiteSpace(exampleText))
                                    {
                                        examples.Add(exampleText);
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

            return examples;
        }

        public static List<string> ExtractSynonyms(string json)
        {
            var synonyms = new List<string>();

            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!IsEnglishEntry(json))
                    return synonyms;

                if (root.TryGetProperty("senses", out var senses) &&
                    senses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sense in senses.EnumerateArray())
                    {
                        if (!IsEnglishSense(sense))
                            continue;

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
            }
            catch
            {
                // Ignore errors
            }

            return synonyms;
        }
    }
}