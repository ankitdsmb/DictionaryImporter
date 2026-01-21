using System.Text.Json;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Handles JSON processing for Kaikki and other JSON-based dictionary sources.
    /// </summary>
    public static class JsonProcessor
    {
        #region Kaikki-Specific Methods (merged from KaikkiJsonHelper)

        /// <summary>
        /// Checks if a JSON element represents an English dictionary entry.
        /// </summary>
        public static bool IsEnglishEntry(JsonElement root)
        {
            // Method 1: Check lang_code
            var langCode = SourceDataHelper.ExtractJsonString(root, "lang_code");
            if (langCode == "en")
                return true;

            // Method 2: Check lang property
            var lang = SourceDataHelper.ExtractJsonString(root, "lang");
            if (!string.IsNullOrWhiteSpace(lang))
            {
                return lang.Equals("English", StringComparison.OrdinalIgnoreCase) ||
                       lang.Contains("english", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Checks if a JSON string represents an English dictionary entry.
        /// </summary>
        public static bool IsEnglishEntry(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                return IsEnglishEntry(doc.RootElement);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a sense JSON element is for English language.
        /// </summary>
        public static bool IsEnglishSense(JsonElement sense)
        {
            var langCode = SourceDataHelper.ExtractJsonString(sense, "lang_code");
            return langCode == "en" || string.IsNullOrWhiteSpace(langCode);
        }

        /// <summary>
        /// Extracts English definitions from a Kaikki JSON element with fallback to raw_glosses.
        /// </summary>
        public static List<string> ExtractEnglishDefinitions(JsonElement root)
        {
            var definitions = new List<string>();
            var senses = SourceDataHelper.ExtractJsonArray(root, "senses");

            if (senses.HasValue)
            {
                foreach (var sense in senses.Value)
                {
                    if (!IsEnglishSense(sense))
                        continue;

                    // Try glosses first
                    var glosses = ExtractGlosses(sense, "glosses");
                    if (glosses.Count > 0)
                    {
                        definitions.AddRange(glosses);
                    }
                    // Fallback to raw_glosses if no regular glosses found
                    else
                    {
                        var rawGlosses = ExtractGlosses(sense, "raw_glosses");
                        definitions.AddRange(rawGlosses);
                    }
                }
            }

            return definitions;
        }

        /// <summary>
        /// Extracts English definitions from a Kaikki JSON string.
        /// </summary>
        public static List<string> ExtractEnglishDefinitions(string json)
        {
            var definitions = new List<string>();

            try
            {
                var doc = JsonDocument.Parse(json);
                if (!IsEnglishEntry(doc.RootElement))
                    return definitions;

                definitions = ExtractEnglishDefinitions(doc.RootElement);
            }
            catch
            {
                // Ignore parsing errors
            }

            return definitions;
        }

        /// <summary>
        /// Extracts etymology from a Kaikki JSON element with support for etymology_templates args.
        /// </summary>
        public static string? ExtractEtymology(JsonElement root)
        {
            // Check etymology_text first
            var etymologyText = SourceDataHelper.ExtractJsonString(root, "etymology_text");
            if (!string.IsNullOrWhiteSpace(etymologyText) && etymologyText.Length > 3)
                return etymologyText;

            // Check etymology_templates with args extraction (from KaikkiJsonHelper)
            return ExtractEtymologyFromTemplates(root);
        }

        /// <summary>
        /// Extracts etymology from a Kaikki JSON string.
        /// </summary>
        public static string? ExtractEtymology(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                return ExtractEtymology(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts examples from a Kaikki JSON element.
        /// </summary>
        public static List<string> ExtractExamples(JsonElement root)
        {
            var examples = new List<string>();
            var senses = SourceDataHelper.ExtractJsonArray(root, "senses");

            if (senses.HasValue)
            {
                foreach (var sense in senses.Value)
                {
                    if (!IsEnglishSense(sense))
                        continue;

                    var examplesArray = SourceDataHelper.ExtractJsonArray(sense, "examples");
                    if (examplesArray.HasValue)
                    {
                        foreach (var example in examplesArray.Value)
                        {
                            var exampleText = SourceDataHelper.ExtractJsonString(example, "text");
                            if (!string.IsNullOrWhiteSpace(exampleText))
                            {
                                examples.Add(exampleText);
                            }
                        }
                    }
                }
            }

            return examples;
        }

        /// <summary>
        /// Extracts examples from a Kaikki JSON string.
        /// </summary>
        public static List<string> ExtractExamples(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                if (!IsEnglishEntry(root))
                    return new List<string>();

                return ExtractExamples(root);
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Extracts synonyms from a Kaikki JSON element.
        /// </summary>
        public static List<string> ExtractSynonyms(JsonElement root)
        {
            var synonyms = new List<string>();
            var senses = SourceDataHelper.ExtractJsonArray(root, "senses");

            if (senses.HasValue)
            {
                foreach (var sense in senses.Value)
                {
                    if (!IsEnglishSense(sense))
                        continue;

                    var synonymsArray = SourceDataHelper.ExtractJsonArray(sense, "synonyms");
                    if (synonymsArray.HasValue)
                    {
                        foreach (var synonym in synonymsArray.Value)
                        {
                            var synonymWord = SourceDataHelper.ExtractJsonString(synonym, "word");
                            if (!string.IsNullOrWhiteSpace(synonymWord))
                            {
                                synonyms.Add(synonymWord);
                            }
                        }
                    }
                }
            }

            return synonyms;
        }

        /// <summary>
        /// Extracts synonyms from a Kaikki JSON string.
        /// </summary>
        public static List<string> ExtractSynonyms(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                if (!IsEnglishEntry(root))
                    return new List<string>();

                return ExtractSynonyms(root);
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Checks if text appears to be a translation list rather than a definition.
        /// </summary>
        public static bool IsTranslationList(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Check for JSON language markers
            if (text.Contains("\"lang\":") || text.Contains("\"lang_code\":"))
                return true;

            // Check for specific language names (from KaikkiJsonHelper)
            return SourceDataHelper.ContainsLanguageMarker(text, "Zulu", "Arabic", "Chinese");
        }

        #endregion Kaikki-Specific Methods (merged from KaikkiJsonHelper)

        #region Additional JSON Processing Methods

        /// <summary>
        /// Extracts part of speech from a Kaikki JSON element.
        /// </summary>
        public static string? ExtractPartOfSpeechFromJson(JsonElement root)
        {
            var pos = SourceDataHelper.ExtractJsonString(root, "pos");
            return !string.IsNullOrWhiteSpace(pos) ? TextNormalizer.NormalizePartOfSpeech(pos) : null;
        }

        /// <summary>
        /// Extracts domain from a Kaikki sense JSON element.
        /// </summary>
        public static string? ExtractDomain(JsonElement sense)
        {
            var categories = SourceDataHelper.ExtractJsonArray(sense, "categories");
            if (categories.HasValue)
            {
                foreach (var category in categories.Value)
                {
                    var categoryName = SourceDataHelper.ExtractJsonString(category, "name");
                    if (!string.IsNullOrWhiteSpace(categoryName))
                        return categoryName;
                }
            }
            return null;
        }

        /// <summary>
        /// Extracts domain from a Kaikki JSON string.
        /// </summary>
        public static string? ExtractDomain(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                var senses = SourceDataHelper.ExtractJsonArray(root, "senses");
                if (senses.HasValue)
                {
                    foreach (var sense in senses.Value)
                    {
                        var domain = ExtractDomain(sense);
                        if (domain != null)
                            return domain;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        /// <summary>
        /// Extracts usage label from a Kaikki sense JSON element.
        /// </summary>
        public static string? ExtractUsageLabel(JsonElement sense)
        {
            var tags = SourceDataHelper.ExtractJsonArray(sense, "tags");
            if (tags.HasValue)
            {
                var tagList = new List<string>();
                foreach (var tag in tags.Value)
                {
                    if (tag.ValueKind == JsonValueKind.String)
                    {
                        tagList.Add(tag.GetString() ?? "");
                    }
                }

                if (tagList.Count > 0)
                    return string.Join(", ", tagList);
            }
            return null;
        }

        /// <summary>
        /// Extracts usage label from a Kaikki JSON string.
        /// </summary>
        public static string? ExtractUsageLabel(string rawFragment)
        {
            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                var senses = SourceDataHelper.ExtractJsonArray(root, "senses");
                if (senses.HasValue)
                {
                    foreach (var sense in senses.Value)
                    {
                        var label = ExtractUsageLabel(sense);
                        if (label != null)
                            return label;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        /// <summary>
        /// Extracts cross references from a Kaikki sense JSON element.
        /// </summary>
        public static List<CrossReference> ExtractCrossReferences(JsonElement sense)
        {
            var crossRefs = new List<CrossReference>();
            var related = SourceDataHelper.ExtractJsonArray(sense, "related");

            if (related.HasValue)
            {
                foreach (var rel in related.Value)
                {
                    var targetWord = SourceDataHelper.ExtractJsonString(rel, "word");
                    if (!string.IsNullOrWhiteSpace(targetWord))
                    {
                        var relationType = SourceDataHelper.ExtractJsonString(rel, "sense") ?? "related";

                        crossRefs.Add(new CrossReference
                        {
                            TargetWord = targetWord,
                            ReferenceType = relationType
                        });
                    }
                }
            }

            return crossRefs;
        }

        /// <summary>
        /// Extracts cross references from a Kaikki JSON string.
        /// </summary>
        public static List<CrossReference> ExtractCrossReferences(string rawFragment)
        {
            var crossRefs = new List<CrossReference>();

            try
            {
                var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                var senses = SourceDataHelper.ExtractJsonArray(root, "senses");
                if (senses.HasValue)
                {
                    foreach (var sense in senses.Value)
                    {
                        crossRefs.AddRange(ExtractCrossReferences(sense));
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return crossRefs;
        }

        #endregion Additional JSON Processing Methods

        #region Private Helper Methods

        /// <summary>
        /// Extracts glosses from a sense element, filtering out translation lists.
        /// </summary>
        private static List<string> ExtractGlosses(JsonElement sense, string propertyName)
        {
            var glosses = new List<string>();
            var glossArray = SourceDataHelper.ExtractJsonArray(sense, propertyName);

            if (glossArray.HasValue)
            {
                foreach (var gloss in glossArray.Value)
                {
                    if (gloss.ValueKind == JsonValueKind.String)
                    {
                        var definition = gloss.GetString();
                        if (!string.IsNullOrWhiteSpace(definition) && !IsTranslationList(definition))
                        {
                            glosses.Add(definition);
                        }
                    }
                }
            }

            return glosses;
        }

        /// <summary>
        /// Extracts etymology from etymology_templates with args extraction (from KaikkiJsonHelper).
        /// </summary>
        private static string? ExtractEtymologyFromTemplates(JsonElement root)
        {
            var templates = SourceDataHelper.ExtractJsonArray(root, "etymology_templates");
            if (!templates.HasValue)
                return null;

            var etymologyParts = new List<string>();

            foreach (var template in templates.Value)
            {
                if (!template.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var arg in args.EnumerateObject())
                {
                    if (IsEtymologyArgument(arg.Name) && arg.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = arg.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            etymologyParts.Add(value);
                    }
                }
            }

            return etymologyParts.Count > 0 ? string.Join(" → ", etymologyParts) : null;
        }

        /// <summary>
        /// Checks if an argument name is related to etymology.
        /// </summary>
        private static bool IsEtymologyArgument(string argName)
        {
            return argName.StartsWith("der", StringComparison.OrdinalIgnoreCase) ||
                   argName.StartsWith("bor", StringComparison.OrdinalIgnoreCase) ||
                   argName.StartsWith("inh", StringComparison.OrdinalIgnoreCase) ||
                   argName.Contains("lang", StringComparison.OrdinalIgnoreCase) ||
                   argName.Contains("etyl", StringComparison.OrdinalIgnoreCase);
        }

        #endregion Private Helper Methods

        #region Advanced JSON Processing Methods

        /// <summary>
        /// Safely parses JSON string and returns root element, handling errors gracefully.
        /// </summary>
        public static JsonElement? SafeParseJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                return doc.RootElement;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts multiple string values from a JSON array property.
        /// </summary>
        public static List<string> ExtractStringArray(JsonElement element, string propertyName)
        {
            var result = new List<string>();
            var array = SourceDataHelper.ExtractJsonArray(element, propertyName);

            if (array.HasValue)
            {
                foreach (var item in array.Value)
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            result.Add(value);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Extracts a nested property value from JSON.
        /// </summary>
        public static string? ExtractNestedProperty(JsonElement root, params string[] propertyPath)
        {
            var current = root;
            foreach (var property in propertyPath)
            {
                if (!current.TryGetProperty(property, out current))
                    return null;
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }

        #endregion Advanced JSON Processing Methods
    }
}