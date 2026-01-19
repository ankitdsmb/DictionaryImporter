using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Comprehensive helper class for dictionary source data processing.
    /// Provides shared functionality for transformers, parsers, and extractors.
    /// </summary>
    public static class SourceDataHelper
    {
        // ADD to SourceDataHelper.cs (replace the broken method)
        // Add this to SourceDataHelper.cs (anywhere in the class)
        public static (bool HasIssues, string IssueDescription) ValidateEntry(DictionaryEntry entry, string sourceCode)
        {
            if (entry == null) return (false, "Entry is null");

            // Special validation for ENG_CHN
            if (sourceCode == "ENG_CHN" && !string.IsNullOrEmpty(entry.Definition))
            {
                var chineseCharCount = CountChineseCharacters(entry.Definition);

                // Check if definition contains Chinese characters (it should for ENG_CHN)
                if (chineseCharCount == 0)
                {
                    // Check if original had Chinese characters
                    if (!string.IsNullOrEmpty(entry.RawFragment))
                    {
                        var originalChineseCount = CountChineseCharacters(entry.RawFragment);
                        if (originalChineseCount > 0)
                        {
                            return (true, $"Lost {originalChineseCount} Chinese characters");
                        }
                    }
                    return (true, "No Chinese characters found in definition");
                }

                // Check for significant data loss
                if (!string.IsNullOrEmpty(entry.RawFragment))
                {
                    var originalLength = entry.RawFragment.Length;
                    var processedLength = entry.Definition.Length;

                    if (processedLength < originalLength * 0.3) // Lost more than 70% of content
                    {
                        return (true, $"Significant content loss: {originalLength} → {processedLength} chars");
                    }
                }
            }

            return (false, string.Empty);
        }

        // ADD method to count Chinese characters
        public static int CountChineseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return Regex.Matches(text, @"[\u4E00-\u9FFF\u3400-\u4DBF\u3000-\u303F\uff00-\uffef]").Count;
        }

        #region Shared Validation and Extraction Methods

        // ADD this method
        public static string NormalizeWordWithSourceContext(string word, string sourceCode)
        {
            return TextNormalizer.NormalizeWordPreservingLanguage(word, sourceCode);
        }

        /// <summary>
        /// Extracts string value from JSON property if it exists and is valid.
        /// </summary>
        public static string? ExtractJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                return !string.IsNullOrWhiteSpace(value) ? value : null;
            }

            return null;
        }

        /// <summary>
        /// Extracts array from JSON property if it exists and is valid.
        /// </summary>
        public static JsonElement.ArrayEnumerator? ExtractJsonArray(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray();
            }

            return null;
        }

        /// <summary>
        /// Validates if a string is a valid dictionary word.
        /// </summary>
        public static bool IsValidDictionaryWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                return false;

            if (!word.Any(char.IsLetter))
                return false;

            return Regex.IsMatch(word, @"^[a-z\s\-']+$");
        }

        /// <summary>
        /// Checks if text contains specific language markers.
        /// </summary>
        public static bool ContainsLanguageMarker(string text, params string[] languages)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var language in languages)
            {
                if (text.Contains(language, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion Shared Validation and Extraction Methods

        #region Source Processing Control Methods

        // Thread-safe per-source processing state
        // Count = number of "allowed" records processed so far
        // LimitReachedLogged = 0/1 int so it can be updated atomically
        private static readonly ConcurrentDictionary<string, ProcessingState> _sourceProcessingState = new();

        private sealed class ProcessingState
        {
            public int Count;
            public int LimitReachedLogged; // 0 = not logged, 1 = logged
        }

        private const int MAX_RECORDS_PER_SOURCE = 25;

        /// <summary>
        /// Controls per-source processing limits.
        /// STRICT FIX:
        /// 1) Count will NOT keep incrementing after max is reached
        /// 2) "Reached maximum..." log prints ONLY once per source
        /// 3) Thread-safe & atomic
        /// </summary>
        public static bool ShouldContinueProcessing(string sourceCode, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return false;

            var state = _sourceProcessingState.GetOrAdd(sourceCode, _ => new ProcessingState());

            // Stop BEFORE increment if already reached max.
            if (Volatile.Read(ref state.Count) >= MAX_RECORDS_PER_SOURCE)
            {
                // Log only once per source (atomic)
                if (logger != null && Volatile.Read(ref state.LimitReachedLogged) == 0)
                {
                    if (Interlocked.Exchange(ref state.LimitReachedLogged, 1) == 0)
                    {
                        logger.LogInformation(
                            "Reached maximum of {MaxRecords} records for {Source} source",
                            MAX_RECORDS_PER_SOURCE,
                            sourceCode);
                    }
                }

                return false;
            }

            // Accept this record and increment count atomically
            var newCount = Interlocked.Increment(ref state.Count);

            // If we incremented past limit (rare race), clamp behavior:
            // We allow exactly MAX_RECORDS_PER_SOURCE records total.
            // If count becomes MAX+1 due to race, return false for this record.
            if (newCount > MAX_RECORDS_PER_SOURCE)
            {
                // Log once
                if (logger != null && Volatile.Read(ref state.LimitReachedLogged) == 0)
                {
                    if (Interlocked.Exchange(ref state.LimitReachedLogged, 1) == 0)
                    {
                        logger.LogInformation(
                            "Reached maximum of {MaxRecords} records for {Source} source",
                            MAX_RECORDS_PER_SOURCE,
                            sourceCode);
                    }
                }

                return false;
            }

            return true;
        }

        public static void ResetProcessingState(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return;

            _sourceProcessingState.TryRemove(sourceCode, out _);
        }

        public static void ResetAllProcessingStates()
        {
            _sourceProcessingState.Clear();
        }

        public static int GetCurrentCount(string sourceCode)
        {
            return _sourceProcessingState.TryGetValue(sourceCode, out var state)
                ? state.Count
                : 0;
        }

        #endregion Source Processing Control Methods

        #region Text Normalization Methods

        /// <summary>
        /// Normalizes dictionary headwords by removing special characters and standardizing.
        /// </summary>
        public static string NormalizeWord(string word)
        {
            return TextNormalizer.NormalizeWord(word);
        }

        /// <summary>
        /// Normalizes part-of-speech tags to a standard format.
        /// </summary>
        public static string NormalizePartOfSpeech(string? pos)
        {
            return TextNormalizer.NormalizePartOfSpeech(pos);
        }

        /// <summary>
        /// General text normalization for cleaning input.
        /// </summary>
        public static string NormalizeText(string input)
        {
            return TextNormalizer.NormalizeText(input);
        }

        /// <summary>
        /// Normalizes dictionary definitions.
        /// </summary>
        public static string? NormalizeDefinition(string text)
        {
            return TextNormalizer.NormalizeDefinition(text);
        }

        #endregion Text Normalization Methods

        #region Kaikki JSON Processing Methods

        public static bool IsEnglishEntry(JsonElement root)
        {
            return JsonProcessor.IsEnglishEntry(root);
        }

        public static bool IsEnglishEntry(string json)
        {
            return JsonProcessor.IsEnglishEntry(json);
        }

        public static bool IsEnglishSense(JsonElement sense)
        {
            return JsonProcessor.IsEnglishSense(sense);
        }

        public static List<string> ExtractEnglishDefinitions(JsonElement root)
        {
            return JsonProcessor.ExtractEnglishDefinitions(root);
        }

        public static List<string> ExtractEnglishDefinitions(string json)
        {
            return JsonProcessor.ExtractEnglishDefinitions(json);
        }

        public static string? ExtractEtymology(JsonElement root)
        {
            return JsonProcessor.ExtractEtymology(root);
        }

        public static string? ExtractEtymology(string rawFragment)
        {
            return JsonProcessor.ExtractEtymology(rawFragment);
        }

        public static string? ExtractPartOfSpeechFromJson(JsonElement root)
        {
            return JsonProcessor.ExtractPartOfSpeechFromJson(root);
        }

        public static List<string> ExtractExamples(JsonElement root)
        {
            return JsonProcessor.ExtractExamples(root);
        }

        public static List<string> ExtractExamples(string rawFragment)
        {
            return JsonProcessor.ExtractExamples(rawFragment);
        }

        public static List<string> ExtractSynonyms(JsonElement root)
        {
            return JsonProcessor.ExtractSynonyms(root);
        }

        public static List<string> ExtractSynonyms(string rawFragment)
        {
            return JsonProcessor.ExtractSynonyms(rawFragment);
        }

        public static string? ExtractDomain(JsonElement sense)
        {
            return JsonProcessor.ExtractDomain(sense);
        }

        public static string? ExtractDomain(string rawFragment)
        {
            return JsonProcessor.ExtractDomain(rawFragment);
        }

        public static string? ExtractUsageLabel(JsonElement sense)
        {
            return JsonProcessor.ExtractUsageLabel(sense);
        }

        public static string? ExtractUsageLabel(string rawFragment)
        {
            return JsonProcessor.ExtractUsageLabel(rawFragment);
        }

        public static List<CrossReference> ExtractCrossReferences(JsonElement sense)
        {
            return JsonProcessor.ExtractCrossReferences(sense);
        }

        public static List<CrossReference> ExtractCrossReferences(string rawFragment)
        {
            return JsonProcessor.ExtractCrossReferences(rawFragment);
        }

        public static bool IsTranslationList(string text)
        {
            return JsonProcessor.IsTranslationList(text);
        }

        #endregion Kaikki JSON Processing Methods

        #region Webster and General Parser Methods

        public static bool IsValidMeaningTitle(string title)
        {
            return ParserHelper.IsValidMeaningTitle(title);
        }

        public static string? ExtractMainDefinition(string definition)
        {
            return ParserHelper.ExtractMainDefinition(definition);
        }

        public static string? ExtractSection(string definition, string marker)
        {
            return ParserHelper.ExtractSection(definition, marker);
        }

        #endregion Webster and General Parser Methods

        #region Text Cleaning and Processing Methods

        public static string CleanEtymologyText(string etymology)
        {
            return TextCleaner.CleanEtymologyText(etymology);
        }

        public static string CleanExampleText(string example)
        {
            return TextCleaner.CleanExampleText(example);
        }

        public static string? DetectLanguageFromEtymology(string etymology)
        {
            return TextCleaner.DetectLanguageFromEtymology(etymology);
        }

        #endregion Text Cleaning and Processing Methods

        #region Helper Creation Methods

        /// <summary>
        /// Creates a fallback parsed definition from a dictionary entry.
        /// </summary>
        public static ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = entry.Definition ?? string.Empty,
                RawFragment = entry.RawFragment ?? string.Empty,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        #endregion Helper Creation Methods

        #region Logging and Error Handling Methods

        public static void LogProgress(ILogger logger, string sourceCode, int count)
        {
            LoggingHelper.LogProgress(logger, sourceCode, count);
        }

        public static void LogMaxReached(ILogger logger, string sourceCode)
        {
            LoggingHelper.LogMaxReached(logger, sourceCode, MAX_RECORDS_PER_SOURCE);
        }

        public static void HandleError(ILogger logger, Exception ex, string sourceCode, string operation)
        {
            LoggingHelper.HandleError(logger, ex, sourceCode, operation);

            // Strict: Reset this source state so a retry run works correctly
            ResetProcessingState(sourceCode);
        }

        #region JsonProcessor Facade Methods

        public static JsonElement? SafeParseJson(string json)
        {
            return JsonProcessor.SafeParseJson(json);
        }

        public static List<string> ExtractStringArray(JsonElement element, string propertyName)
        {
            return JsonProcessor.ExtractStringArray(element, propertyName);
        }

        public static string? ExtractNestedProperty(JsonElement root, params string[] propertyPath)
        {
            return JsonProcessor.ExtractNestedProperty(root, propertyPath);
        }

        #endregion JsonProcessor Facade Methods

        #endregion Logging and Error Handling Methods
    }
}