// KaikkiParsingHelper.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Handles JSON processing for Kaikki and other JSON-based dictionary sources.
    /// </summary>
    public static class ParsingHelperKaikki
    {
        #region Kaikki-Specific Methods

        /// <summary>
        /// Checks if a JSON element represents an English dictionary entry.
        /// </summary>
        public static bool IsEnglishEntry(JsonElement root)
        {
            var langCode = SourceDataHelper.ExtractJsonString(root, "lang_code");
            if (string.Equals(langCode, "en", StringComparison.OrdinalIgnoreCase))
                return true;

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
                using var doc = JsonDocument.Parse(json);
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
            return string.IsNullOrWhiteSpace(langCode) ||
                   string.Equals(langCode, "en", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts English definitions from a Kaikki JSON element with fallback to raw_glosses.
        /// </summary>
        public static List<string> ExtractEnglishDefinitions(JsonElement root)
        {
            var definitions = new List<string>();
            var senses = SourceDataHelper.ExtractJsonArray(root, "senses");

            if (!senses.HasValue)
                return definitions;

            foreach (var sense in senses.Value)
            {
                if (!IsEnglishSense(sense))
                    continue;

                var glosses = ExtractGlosses(sense, "glosses");
                if (glosses.Count > 0)
                {
                    definitions.AddRange(glosses);
                    continue;
                }

                var rawGlosses = ExtractGlosses(sense, "raw_glosses");
                if (rawGlosses.Count > 0)
                    definitions.AddRange(rawGlosses);
            }

            return DedupeAndNormalizeTextList(definitions);
        }

        /// <summary>
        /// Extracts English definitions from a Kaikki JSON string.
        /// </summary>
        public static List<string> ExtractEnglishDefinitions(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);

                if (!IsEnglishEntry(doc.RootElement))
                    return new List<string>();

                return ExtractEnglishDefinitions(doc.RootElement);
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Extracts etymology from a Kaikki JSON element with support for etymology_templates args.
        /// </summary>
        public static string? ExtractEtymology(JsonElement root)
        {
            var etymologyText = SourceDataHelper.ExtractJsonString(root, "etymology_text");
            if (!string.IsNullOrWhiteSpace(etymologyText) && etymologyText.Length > 3)
            {
                etymologyText = NormalizeBrokenHtmlEntities(etymologyText);
                etymologyText = CleanKaikkiText(etymologyText);
                etymologyText = SafeTruncate(etymologyText, 4000);
                return string.IsNullOrWhiteSpace(etymologyText) ? null : etymologyText;
            }

            var fromTemplates = ExtractEtymologyFromTemplates(root);
            if (string.IsNullOrWhiteSpace(fromTemplates))
                return null;

            fromTemplates = NormalizeBrokenHtmlEntities(fromTemplates);
            fromTemplates = CleanKaikkiText(fromTemplates);
            fromTemplates = SafeTruncate(fromTemplates, 4000);

            return string.IsNullOrWhiteSpace(fromTemplates) ? null : fromTemplates;
        }

        /// <summary>
        /// Extracts etymology from a Kaikki JSON string.
        /// </summary>
        public static string? ExtractEtymology(string rawFragment)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawFragment);
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

            if (!senses.HasValue)
                return examples;

            foreach (var sense in senses.Value)
            {
                if (!IsEnglishSense(sense))
                    continue;

                var examplesArray = SourceDataHelper.ExtractJsonArray(sense, "examples");
                if (!examplesArray.HasValue)
                    continue;

                foreach (var example in examplesArray.Value)
                {
                    var exampleText = SourceDataHelper.ExtractJsonString(example, "text");
                    if (string.IsNullOrWhiteSpace(exampleText))
                        continue;

                    exampleText = NormalizeBrokenHtmlEntities(exampleText);
                    exampleText = CleanKaikkiText(exampleText);

                    if (string.IsNullOrWhiteSpace(exampleText))
                        continue;

                    if (!IsAcceptableEnglishText(exampleText))
                        continue;

                    exampleText = SafeTruncate(exampleText, 800);
                    if (string.IsNullOrWhiteSpace(exampleText))
                        continue;

                    examples.Add(exampleText);
                }
            }

            return DedupeAndNormalizeTextList(examples);
        }

        /// <summary>
        /// Extracts examples from a Kaikki JSON string.
        /// </summary>
        public static List<string> ExtractExamples(string rawFragment)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawFragment);
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

            if (!senses.HasValue)
                return synonyms;

            foreach (var sense in senses.Value)
            {
                if (!IsEnglishSense(sense))
                    continue;

                var synonymsArray = SourceDataHelper.ExtractJsonArray(sense, "synonyms");
                if (!synonymsArray.HasValue)
                    continue;

                foreach (var synonym in synonymsArray.Value)
                {
                    var synonymWord = SourceDataHelper.ExtractJsonString(synonym, "word");
                    if (string.IsNullOrWhiteSpace(synonymWord))
                        continue;

                    synonymWord = NormalizeBrokenHtmlEntities(synonymWord);
                    synonymWord = CleanKaikkiText(synonymWord);

                    if (!IsAcceptableSynonym(synonymWord))
                        continue;

                    synonyms.Add(synonymWord);
                }
            }

            return DedupeAndNormalizeTextList(synonyms);
        }

        /// <summary>
        /// Extracts synonyms from a Kaikki JSON string.
        /// </summary>
        public static List<string> ExtractSynonyms(string rawFragment)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawFragment);
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

            if (text.Contains("\"lang\":", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("\"lang_code\":", StringComparison.OrdinalIgnoreCase))
                return true;

            if (SourceDataHelper.ContainsLanguageMarker(text, "Zulu", "Arabic", "Chinese"))
                return true;

            // NEW: common Kaikki/Wiktionary translation table patterns
            if (text.Contains("Translations", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Regex.IsMatch(text, @"\b(tr|transl|translation)\b", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        #endregion

        #region Additional JSON Processing Methods

        /// <summary>
        /// Extracts part of speech from a Kaikki JSON element.
        /// </summary>
        public static string? ExtractPartOfSpeechFromJson(JsonElement root)
        {
            var pos = SourceDataHelper.ExtractJsonString(root, "pos");
            return !string.IsNullOrWhiteSpace(pos) ? TextProcessingHelper.NormalizePartOfSpeech(pos) : null;
        }

        /// <summary>
        /// Extracts domain from a Kaikki sense JSON element.
        /// </summary>
        public static string? ExtractDomain(JsonElement sense)
        {
            var categories = SourceDataHelper.ExtractJsonArray(sense, "categories");
            if (!categories.HasValue)
                return null;

            foreach (var category in categories.Value)
            {
                var categoryName = SourceDataHelper.ExtractJsonString(category, "name");
                if (string.IsNullOrWhiteSpace(categoryName))
                    continue;

                categoryName = NormalizeBrokenHtmlEntities(categoryName);
                categoryName = CleanKaikkiText(categoryName);
                categoryName = SafeTruncate(categoryName, 150);

                if (!string.IsNullOrWhiteSpace(categoryName))
                    return categoryName;
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
                using var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                var senses = SourceDataHelper.ExtractJsonArray(root, "senses");
                if (!senses.HasValue)
                    return null;

                foreach (var sense in senses.Value)
                {
                    var domain = ExtractDomain(sense);
                    if (!string.IsNullOrWhiteSpace(domain))
                        return domain;
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Extracts usage label from a Kaikki sense JSON element.
        /// </summary>
        public static string? ExtractUsageLabel(JsonElement sense)
        {
            var tags = SourceDataHelper.ExtractJsonArray(sense, "tags");
            if (!tags.HasValue)
                return null;

            var tagList = new List<string>();
            foreach (var tag in tags.Value)
            {
                if (tag.ValueKind != JsonValueKind.String)
                    continue;

                var t = tag.GetString();
                if (string.IsNullOrWhiteSpace(t))
                    continue;

                t = NormalizeBrokenHtmlEntities(t.Trim());
                t = CleanKaikkiText(t);
                t = SafeTruncate(t, 200);

                if (string.IsNullOrWhiteSpace(t))
                    continue;

                tagList.Add(t);
            }

            return tagList.Count > 0 ? string.Join(", ", tagList.Distinct(StringComparer.OrdinalIgnoreCase)) : null;
        }

        /// <summary>
        /// Extracts usage label from a Kaikki JSON string.
        /// </summary>
        public static string? ExtractUsageLabel(string rawFragment)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                var senses = SourceDataHelper.ExtractJsonArray(root, "senses");
                if (!senses.HasValue)
                    return null;

                foreach (var sense in senses.Value)
                {
                    var label = ExtractUsageLabel(sense);
                    if (!string.IsNullOrWhiteSpace(label))
                        return label;
                }
            }
            catch
            {
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

            if (!related.HasValue)
                return crossRefs;

            foreach (var rel in related.Value)
            {
                var targetWord = SourceDataHelper.ExtractJsonString(rel, "word");
                if (string.IsNullOrWhiteSpace(targetWord))
                    continue;

                targetWord = NormalizeBrokenHtmlEntities(targetWord);
                targetWord = CleanKaikkiText(targetWord);

                if (!IsAcceptableSynonym(targetWord))
                    continue;

                var relationType = SourceDataHelper.ExtractJsonString(rel, "sense") ?? "related";
                relationType = NormalizeBrokenHtmlEntities(relationType);
                relationType = CleanKaikkiText(relationType);
                relationType = SafeTruncate(relationType, 60);

                crossRefs.Add(new CrossReference
                {
                    TargetWord = targetWord,
                    ReferenceType = string.IsNullOrWhiteSpace(relationType) ? "related" : relationType
                });
            }

            return crossRefs
                .GroupBy(x => $"{x.TargetWord}|{x.ReferenceType}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// Extracts cross references from a Kaikki JSON string.
        /// </summary>
        public static List<CrossReference> ExtractCrossReferences(string rawFragment)
        {
            var crossRefs = new List<CrossReference>();

            try
            {
                using var doc = JsonDocument.Parse(rawFragment);
                var root = doc.RootElement;

                var senses = SourceDataHelper.ExtractJsonArray(root, "senses");
                if (!senses.HasValue)
                    return crossRefs;

                foreach (var sense in senses.Value)
                    crossRefs.AddRange(ExtractCrossReferences(sense));
            }
            catch
            {
            }

            return crossRefs
                .GroupBy(x => $"{x.TargetWord}|{x.ReferenceType}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        #endregion

        #region Synonym Validation (kept signatures)

        public static bool ValidateSynonymPair(string headwordA, string headwordB)
        {
            if (string.IsNullOrWhiteSpace(headwordA) || string.IsNullOrWhiteSpace(headwordB))
                return false;

            var a = headwordA.ToLowerInvariant().Trim();
            var b = headwordB.ToLowerInvariant().Trim();

            if (a == b)
                return false;

            if (!IsValidHeadword(a) || !IsValidHeadword(b))
                return false;

            if (a.Length < 2 || b.Length < 2)
                return false;

            var antonyms = new[]
            {
                ("big", "small"), ("hot", "cold"), ("up", "down"),
                ("good", "bad"), ("yes", "no"), ("black", "white"),
                ("day", "night"), ("fast", "slow"), ("high", "low"),
                ("love", "hate"), ("rich", "poor"), ("strong", "weak")
            };

            return !antonyms.Any(p =>
                (p.Item1 == a && p.Item2 == b) ||
                (p.Item1 == b && p.Item2 == a));
        }

        private static bool IsValidHeadword(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                return false;

            if (!word.Any(char.IsLetter))
                return false;

            return Regex.IsMatch(word, @"^[a-z\s\-']+$");
        }

        #endregion

        #region Sense Helpers

        public static string? ExtractDefinitionFromSense(JsonElement sense)
        {
            // 1) Prefer glosses
            if (sense.TryGetProperty("glosses", out var glosses) && glosses.ValueKind == JsonValueKind.Array)
            {
                foreach (var gloss in glosses.EnumerateArray())
                {
                    if (gloss.ValueKind != JsonValueKind.String)
                        continue;

                    var definition = gloss.GetString()?.Trim();
                    definition = NormalizeBrokenHtmlEntities(definition ?? string.Empty);
                    definition = CleanKaikkiText(definition);

                    definition = NormalizeDefinitionForStorage(definition);

                    if (!IsAcceptableDefinition(definition))
                        continue;

                    return definition;
                }
            }

            // 2) Fallback to raw_glosses
            if (sense.TryGetProperty("raw_glosses", out var rawGlosses) && rawGlosses.ValueKind == JsonValueKind.Array)
            {
                foreach (var rawGloss in rawGlosses.EnumerateArray())
                {
                    if (rawGloss.ValueKind != JsonValueKind.String)
                        continue;

                    var definition = rawGloss.GetString()?.Trim();
                    definition = NormalizeBrokenHtmlEntities(definition ?? string.Empty);
                    definition = CleanKaikkiText(definition);

                    definition = NormalizeDefinitionForStorage(definition);

                    if (!IsAcceptableDefinition(definition))
                        continue;

                    return definition;
                }
            }

            return null;
        }

        public static List<string> ExtractSynonymsList(JsonElement sense)
        {
            var synonyms = new List<string>();

            if (sense.TryGetProperty("synonyms", out var synonymsArray) && synonymsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var synonym in synonymsArray.EnumerateArray())
                {
                    if (synonym.ValueKind != JsonValueKind.Object)
                        continue;

                    if (!synonym.TryGetProperty("word", out var word) || word.ValueKind != JsonValueKind.String)
                        continue;

                    var synonymWord = word.GetString();
                    synonymWord = NormalizeBrokenHtmlEntities(synonymWord ?? string.Empty);
                    synonymWord = CleanKaikkiText(synonymWord);

                    synonymWord = NormalizeWordToken(synonymWord);

                    if (!IsAcceptableSynonym(synonymWord))
                        continue;

                    synonyms.Add(synonymWord);
                }
            }

            return DedupeAndNormalizeTextList(synonyms);
        }

        #endregion

        #region Private Helper Methods

        private static List<string> ExtractGlosses(JsonElement sense, string propertyName)
        {
            var glosses = new List<string>();
            var glossArray = SourceDataHelper.ExtractJsonArray(sense, propertyName);

            if (!glossArray.HasValue)
                return glosses;

            foreach (var gloss in glossArray.Value)
            {
                if (gloss.ValueKind != JsonValueKind.String)
                    continue;

                var definition = gloss.GetString();
                definition = NormalizeBrokenHtmlEntities(definition ?? string.Empty);
                definition = CleanKaikkiText(definition);

                definition = NormalizeDefinitionForStorage(definition);

                if (!IsAcceptableDefinition(definition))
                    continue;

                glosses.Add(definition);
            }

            return glosses;
        }

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
                        value = NormalizeBrokenHtmlEntities(value ?? string.Empty);
                        value = CleanKaikkiText(value);
                        value = SafeTruncate(value, 120);

                        if (!string.IsNullOrWhiteSpace(value))
                            etymologyParts.Add(value);
                    }
                }
            }

            return etymologyParts.Count > 0 ? string.Join(" → ", etymologyParts) : null;
        }

        private static bool IsEtymologyArgument(string argName)
        {
            return argName.StartsWith("der", StringComparison.OrdinalIgnoreCase) ||
                   argName.StartsWith("bor", StringComparison.OrdinalIgnoreCase) ||
                   argName.StartsWith("inh", StringComparison.OrdinalIgnoreCase) ||
                   argName.Contains("lang", StringComparison.OrdinalIgnoreCase) ||
                   argName.Contains("etyl", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Advanced JSON Processing Methods (kept signatures)

        public static JsonElement? SafeParseJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone(); // IMPORTANT: clone because doc will dispose
            }
            catch
            {
                return null;
            }
        }

        public static List<string> ExtractStringArray(JsonElement element, string propertyName)
        {
            var result = new List<string>();
            var array = SourceDataHelper.ExtractJsonArray(element, propertyName);

            if (!array.HasValue)
                return result;

            foreach (var item in array.Value)
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;

                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value.Trim());
            }

            return result;
        }

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

        #endregion

        #region NEW METHODS (added)

        // NEW METHOD (added)
        public static bool IsJsonRawFragment(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.TrimStart();
            return raw.StartsWith("{", StringComparison.Ordinal) || raw.StartsWith("[", StringComparison.Ordinal);
        }

        // NEW METHOD (added)
        public static bool TryParseJsonRoot(string raw, out JsonElement root)
        {
            root = default;

            if (!IsJsonRawFragment(raw))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                root = doc.RootElement.Clone();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // NEW METHOD (added)
        public static string NormalizeBrokenHtmlEntities(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = text;

            result = Regex.Replace(result, @"(?<!&)#(?<num>\d+);", @"&#${num};");
            result = WebUtility.HtmlDecode(result);

            return result;
        }

        // NEW METHOD (added)
        public static bool ContainsBrokenNumericEntities(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"(?<!&)#\d+;");
        }

        // NEW METHOD (added)
        public static string CleanKaikkiText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var t = text;

            // Remove common wiki template blocks
            t = Regex.Replace(t, @"\{\{[^{}]*\}\}", " ", RegexOptions.Singleline);

            // Best-effort: nested template cleanup (limit iterations)
            for (var i = 0; i < 3; i++)
            {
                var before = t;
                t = Regex.Replace(t, @"\{\{.*?\}\}", " ", RegexOptions.Singleline);
                if (t == before)
                    break;
            }

            // Convert [[link|label]] => label, [[word]] => word
            t = Regex.Replace(t, @"\[\[([^\|\]]+)\|([^\]]+)\]\]", "$2", RegexOptions.Singleline);
            t = Regex.Replace(t, @"\[\[([^\]]+)\]\]", "$1", RegexOptions.Singleline);

            // Strip common emphasis markup
            t = t.Replace("'''", string.Empty).Replace("''", string.Empty);

            // Remove ref/html-ish blocks
            t = Regex.Replace(t, @"<ref[^>]*>.*?</ref>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<[^>]+>", " ", RegexOptions.Singleline);

            // Remove leftover pipes and braces noise
            t = t.Replace("|", " ");
            t = t.Replace("{", " ").Replace("}", " ");

            // Normalize whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            return t;
        }

        // NEW METHOD (added)
        public static bool IsAcceptableEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Keep IPA + math symbols; filter heavy non-Latin scripts
            var latinLetters = 0;
            var nonLatinLetters = 0;

            foreach (var c in text)
            {
                if (!char.IsLetter(c))
                    continue;

                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    latinLetters++;
                else
                    nonLatinLetters++;
            }

            // If mostly non-latin letters -> reject (but allow short cases)
            if (nonLatinLetters >= 5 && nonLatinLetters > latinLetters)
                return false;

            // Explicit blocks: CJK + Hangul + Hiragana/Katakana
            if (Regex.IsMatch(text, @"[\u4E00-\u9FFF\u3040-\u30FF\uAC00-\uD7AF]"))
                return false;

            return true;
        }

        // NEW METHOD (added)
        public static bool IsAcceptableSynonym(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var t = NormalizeWordToken(text);

            if (t.Length < 2 || t.Length > 60)
                return false;

            if (!Regex.IsMatch(t, @"[A-Za-z]"))
                return false;

            // Avoid URLs / junk
            if (t.Contains("http", StringComparison.OrdinalIgnoreCase))
                return false;

            // Reject obvious template/junk remnants
            if (t.Contains("{{") || t.Contains("}}") || t.Contains("[[") || t.Contains("]]"))
                return false;

            // Allow hyphen/apostrophe/spaces
            if (!Regex.IsMatch(t, @"^[A-Za-z\s\-'’]+$"))
                return false;

            if (!IsAcceptableEnglishText(t))
                return false;

            return true;
        }

        // NEW METHOD (added)
        public static bool IsTooLong(string text)
        {
            return text != null && text.Length > 2000;
        }

        // NEW METHOD (added)
        public static List<string> DedupeAndNormalizeTextList(IEnumerable<string> items)
        {
            if (items == null)
                return new List<string>();

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();

            foreach (var item in items)
            {
                var t = item?.Trim();
                if (string.IsNullOrWhiteSpace(t))
                    continue;

                if (set.Add(t))
                    list.Add(t);
            }

            return list;
        }

        // NEW METHOD (added)
        public static string SafeTruncate(string text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (maxLen < 10)
                return text.Trim();

            if (text.Length <= maxLen)
                return text.Trim();

            // Keep stable and avoid cutting mid-word too aggressively
            var slice = text.Substring(0, maxLen).TrimEnd();

            // Try last sentence boundary
            var lastPeriod = slice.LastIndexOf('.', slice.Length - 1);
            if (lastPeriod >= 80)
                return slice.Substring(0, lastPeriod + 1).Trim();

            // Try last whitespace boundary
            var lastSpace = slice.LastIndexOf(' ');
            if (lastSpace >= 60)
                return slice.Substring(0, lastSpace).Trim();

            return slice.Trim();
        }

        // NEW METHOD (added)
        public static bool IsAcceptableDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return false;

            if (IsTranslationList(definition))
                return false;

            if (!IsAcceptableEnglishText(definition))
                return false;

            // prevent template garbage that survived cleaning
            if (ContainsMostlyPunctuation(definition))
                return false;

            // Length safety - truncate rather than drop elsewhere
            if (definition.Length > 5000)
                return false;

            return true;
        }

        // NEW METHOD (added)
        public static string NormalizeDefinitionForStorage(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            var d = definition.Trim();

            // Remove starting bullet markers or numbering from wiki dumps
            d = Regex.Replace(d, @"^\s*[\*\-\u2022]+\s*", string.Empty);
            d = Regex.Replace(d, @"^\s*\(\s*\d+\s*\)\s*", string.Empty);

            // Normalize repeated separators
            d = Regex.Replace(d, @"\s*;\s*;", "; ");
            d = Regex.Replace(d, @"\s{2,}", " ").Trim();

            // Final safety truncation
            d = SafeTruncate(d, 2000);

            return d;
        }

        // NEW METHOD (added)
        public static string NormalizeWordToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return token;

            var t = token.Trim();

            // Remove surrounding punctuation
            t = t.Trim('\"', '\'', '“', '”', '‘', '’', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}');

            // Collapse internal whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            return t;
        }

        // NEW METHOD (added)
        private static bool ContainsMostlyPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var lettersOrDigits = 0;
            var total = 0;

            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                    continue;

                total++;
                if (char.IsLetterOrDigit(c))
                    lettersOrDigits++;
            }

            if (total == 0)
                return true;

            // If < 15% letters/digits, it's likely junk
            return (lettersOrDigits * 100 / total) < 15;
        }

        // NEW METHOD (added)
        public static string BuildStableSenseDedupeKey(string headword, int senseNumber, string? domain, string? usageLabel, string? definition)
        {
            headword ??= string.Empty;
            definition ??= string.Empty;
            domain ??= string.Empty;
            usageLabel ??= string.Empty;

            var normalized = $"{headword.Trim().ToLowerInvariant()}|{senseNumber}|{domain.Trim().ToLowerInvariant()}|{usageLabel.Trim().ToLowerInvariant()}|{NormalizeForHash(definition)}";
            return normalized;
        }

        // NEW METHOD (added)
        public static string NormalizeForHash(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var t = text.Trim().ToLowerInvariant();
            t = Regex.Replace(t, @"\s+", " ");
            t = t.Replace("’", "'");

            // Remove quotes around same content
            t = t.Trim('\"', '\'');

            // Keep it bounded
            if (t.Length > 512)
                t = t.Substring(0, 512);

            return t;
        }
        // NEW METHOD (added)
        public static bool IsLikelyJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        // NEW METHOD (added)
        public static bool TryParseRoot(string rawFragment, out JsonElement root)
        {
            root = default;

            if (string.IsNullOrWhiteSpace(rawFragment))
                return false;

            if (!IsLikelyJson(rawFragment))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(rawFragment);
                root = doc.RootElement.Clone(); // important: safe after JsonDocument disposed
                return true;
            }
            catch
            {
                return false;
            }
        }
        // NEW METHOD (added)
        public static bool TryParseEnglishRoot(string rawFragment, out JsonElement root)
        {
            root = default;

            if (!TryParseRoot(rawFragment, out root))
                return false;

            return IsEnglishEntry(root);
        }

        #endregion


        private static readonly char[] _quoteChars = { '"', '\'', '`', '«', '»', '「', '」', '『', '』' };
        private static readonly string[] _templateMarkers = { "{{", "}}", "[[", "]]" };

        private static readonly Dictionary<string, string> _languagePatterns = new()
        {
            { @"\bLatin\b", "la" },
            { @"\bAncient Greek\b|\bGreek\b", "el" },
            { @"\bFrench\b", "fr" },
            { @"\bGerman(ic)?\b", "de" },
            { @"\bOld English\b", "ang" },
            { @"\bMiddle English\b", "enm" },
            { @"\bItalian\b", "it" },
            { @"\bSpanish\b", "es" },
            { @"\bDutch\b", "nl" },
            { @"\bProto-Indo-European\b", "ine-pro" },
            { @"\bOld Norse\b", "non" },
            { @"\bOld French\b", "fro" },
            { @"\bAnglo-Norman\b", "xno" }
        };

        /// <summary>
        /// Cleans etymology text by removing template markers and HTML.
        /// </summary>
        public static string CleanEtymologyText(string etymology)
        {
            if (string.IsNullOrWhiteSpace(etymology))
                return string.Empty;

            var cleaned = Regex.Replace(etymology, @"\s+", " ").Trim();

            foreach (var marker in _templateMarkers)
            {
                cleaned = cleaned.Replace(marker, "");
            }

            cleaned = Regex.Replace(cleaned, @"<[^>]+>", "");

            return cleaned.Trim();
        }

        /// <summary>
        /// Cleans example text by removing quotes and translations.
        /// </summary>
        public static string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            var cleaned = example.Trim(_quoteChars);
            cleaned = Regex.Replace(cleaned, @"\s*\([^)]*\)\s*", " ");

            if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
            {
                cleaned += ".";
            }

            return cleaned.Trim();
        }

        /// <summary>
        /// Detects language code from etymology text.
        /// </summary>
        public static string? DetectLanguageFromEtymology(string etymology)
        {
            if (string.IsNullOrWhiteSpace(etymology))
                return null;

            foreach (var pattern in _languagePatterns)
            {
                if (Regex.IsMatch(etymology, pattern.Key, RegexOptions.IgnoreCase))
                {
                    return pattern.Value;
                }
            }

            return null;
        }
    }
}
