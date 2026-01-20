using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Common.Helper
{
    public static class SourceDataHelper
    {
        // Add to SourceDataHelper.cs (or create new helper)
        public static class BilingualTextPreserver
        {
            private static readonly HashSet<string> BilingualSources = new()
            {
                "ENG_CHN", "CENTURY21", "ENG_COLLINS"
            };

            public static string PreserveBilingualContent(string text, string sourceCode)
            {
                if (string.IsNullOrWhiteSpace(text) || !BilingualSources.Contains(sourceCode))
                    return text;

                // For bilingual sources, only do minimal cleaning
                text = Regex.Replace(text, @"\s+", " ").Trim();

                // Decode HTML entities if present
                if (text.Contains('&'))
                {
                    text = System.Net.WebUtility.HtmlDecode(text);
                }

                return text;
            }

            public static bool ShouldPreserveChinese(string sourceCode)
            {
                return BilingualSources.Contains(sourceCode);
            }
        }

        #region Entry Validation

        // In SourceDataHelper.cs - Update the method
        public static string NormalizeDefinitionForSource(string definition, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            // Special handling for bilingual sources
            if (BilingualTextPreserver.ShouldPreserveChinese(sourceCode))
            {
                return BilingualTextPreserver.PreserveBilingualContent(definition, sourceCode);
            }

            // Original logic for other sources
            return NormalizeDefinition(definition);
        }

        // MODIFY: Update existing NormalizeDefinition to be backward compatible
        public static string NormalizeDefinition(string definition, string sourceCode = null)
        {
            if (string.IsNullOrWhiteSpace(definition)) return definition;
            // If source code provided, use source-aware normalization
            if (!string.IsNullOrWhiteSpace(sourceCode))
            {
                return NormalizeDefinitionForSource(definition, sourceCode);
            }
            // Fallback to original generic normalization for backward compatibility
            var normalized = definition
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace(" ", " ")
                .Trim();
            normalized = Regex.Replace(normalized, @"[^\x00-\x7F\s\.\,\;\:\!\?\(\)\[\]\-\'""\&\@\#\$\%\^\*]", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        // ADD: Helper method to check for Chinese characters (simpler version)
        public static bool ContainsChineseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            // Check for Chinese characters using Unicode ranges
            foreach (char c in text)
            {
                int code = (int)c;
                if ((code >= 0x4E00 && code <= 0x9FFF) || // CJK Unified Ideographs
                    (code >= 0x3400 && code <= 0x4DBF) || // CJK Extension A
                    (code >= 0x3000 && code <= 0x303F))   // CJK Symbols and Punctuation
                {
                    return true;
                }
            }
            return false;
        }

        #endregion Entry Validation

        #region Shared Validation and Extraction

        public static string NormalizeWordWithSourceContext(string word, string sourceCode)
        {
            return TextNormalizer.NormalizeWordPreservingLanguage(word, sourceCode);
        }

        public static string? ExtractJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                return !string.IsNullOrWhiteSpace(value) ? value : null;
            }
            return null;
        }

        public static JsonElement.ArrayEnumerator? ExtractJsonArray(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray();
            }
            return null;
        }

        public static bool ContainsLanguageMarker(string text, params string[] languages)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var language in languages)
            {
                if (text.Contains(language, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        #endregion Shared Validation and Extraction

        #region Source Processing Control

        private static readonly ConcurrentDictionary<string, ProcessingState> _sourceProcessingState = new();

        private sealed class ProcessingState
        {
            public int Count;
            public int LimitReachedLogged;
        }

        private const int MAX_RECORDS_PER_SOURCE = 25;

        public static bool ShouldContinueProcessing(string sourceCode, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(sourceCode)) return false;
            var state = _sourceProcessingState.GetOrAdd(sourceCode, _ => new ProcessingState());
            if (Volatile.Read(ref state.Count) >= MAX_RECORDS_PER_SOURCE)
            {
                if (logger != null && Volatile.Read(ref state.LimitReachedLogged) == 0)
                {
                    if (Interlocked.Exchange(ref state.LimitReachedLogged, 1) == 0)
                    {
                        logger.LogInformation(
                            "Reached maximum of {MaxRecords} records for {Source} source",
                            MAX_RECORDS_PER_SOURCE, sourceCode);
                    }
                }
                return false;
            }
            var newCount = Interlocked.Increment(ref state.Count);
            if (newCount > MAX_RECORDS_PER_SOURCE)
            {
                if (logger != null && Volatile.Read(ref state.LimitReachedLogged) == 0)
                {
                    if (Interlocked.Exchange(ref state.LimitReachedLogged, 1) == 0)
                    {
                        logger.LogInformation(
                            "Reached maximum of {MaxRecords} records for {Source} source",
                            MAX_RECORDS_PER_SOURCE, sourceCode);
                    }
                }
                return false;
            }
            return true;
        }

        public static void ResetProcessingState(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode)) return;
            _sourceProcessingState.TryRemove(sourceCode, out _);
        }

        public static int GetCurrentCount(string sourceCode)
        {
            return _sourceProcessingState.TryGetValue(sourceCode, out var state) ? state.Count : 0;
        }

        #endregion Source Processing Control

        #region Text Normalization

        public static string NormalizeWord(string word)
        {
            return TextNormalizer.NormalizeWord(word);
        }

        public static string NormalizePartOfSpeech(string? pos)
        {
            return TextNormalizer.NormalizePartOfSpeech(pos);
        }

        #endregion Text Normalization

        #region Kaikki JSON Processing

        public static string? ExtractEtymology(string rawFragment)
        {
            return JsonProcessor.ExtractEtymology(rawFragment);
        }

        public static List<string> ExtractExamples(string rawFragment)
        {
            return JsonProcessor.ExtractExamples(rawFragment);
        }

        public static List<string> ExtractSynonyms(string rawFragment)
        {
            return JsonProcessor.ExtractSynonyms(rawFragment);
        }

        public static string? ExtractDomain(JsonElement sense)
        {
            return JsonProcessor.ExtractDomain(sense);
        }

        public static string? ExtractUsageLabel(JsonElement sense)
        {
            return JsonProcessor.ExtractUsageLabel(sense);
        }

        public static List<CrossReference> ExtractCrossReferences(JsonElement sense)
        {
            return JsonProcessor.ExtractCrossReferences(sense);
        }

        #endregion Kaikki JSON Processing

        #region Webster and General Parser

        public static string? ExtractMainDefinition(string definition)
        {
            return ParserHelper.ExtractMainDefinition(definition);
        }

        public static string? ExtractSection(string definition, string marker)
        {
            return ParserHelper.ExtractSection(definition, marker);
        }

        #endregion Webster and General Parser

        #region Text Cleaning and Language Detection

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

        #endregion Text Cleaning and Language Detection

        #region Helper Creation

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

        #endregion Helper Creation

        #region Logging and Error Handling

        public static void LogProgress(ILogger logger, string sourceCode, int count)
        {
            LoggingHelper.LogProgress(logger, sourceCode, count);
        }

        public static void HandleError(ILogger logger, Exception ex, string sourceCode, string operation)
        {
            LoggingHelper.HandleError(logger, ex, sourceCode, operation);
            ResetProcessingState(sourceCode);
        }

        #endregion Logging and Error Handling
    }
}