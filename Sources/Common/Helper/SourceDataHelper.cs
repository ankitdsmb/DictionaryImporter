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

        public static string? ExtractProperDomain(string sourceCode, string? rawDomain, string definition)
        {
            if (string.IsNullOrWhiteSpace(rawDomain)) return null;

            var domain = rawDomain.Trim();

            switch (sourceCode)
            {
                case "ENG_OXFORD":
                    // Oxford: (informal, chiefly N. Amer.)definition
                    var oxfordMatch = Regex.Match(definition, @"^\(([^)]+)\)");
                    if (oxfordMatch.Success)
                    {
                        var oxfordDomain = oxfordMatch.Groups[1].Value.Trim();
                        // Clean: remove any trailing punctuation that's part of definition
                        oxfordDomain = oxfordDomain.Split('.')[0].Trim();
                        return oxfordDomain.Length <= 100 ? oxfordDomain : oxfordDomain.Substring(0, 100);
                    }
                    return null;

                case "ENG_COLLINS":
                    // Collins: 【语域标签】：mainly AM 主美
                    if (domain.StartsWith("【语域标签】："))
                    {
                        domain = domain.Substring("【语域标签】：".Length).Trim();
                        // Take only English part before Chinese
                        var parts = domain.Split(' ');
                        if (parts.Length > 0) return parts[0].Trim();
                    }
                    // Also check for register patterns in definition
                    if (definition.Contains("主美") || definition.Contains("美式")) return "US";
                    if (definition.Contains("主英") || definition.Contains("英式")) return "UK";
                    if (definition.Contains("正式")) return "FORMAL";
                    if (definition.Contains("非正式")) return "INFORMAL";
                    return null;

                case "STRUCT_JSON":
                case "KAIKKI":
                    // JSON sources: use field directly, but clean it
                    domain = Regex.Replace(domain, @"[<>\(\)]", "").Trim();
                    return domain.Length <= 50 ? domain : domain.Substring(0, 50);

                case "GUT_WEBSTER":
                    // Gutenberg: <Mus> or (Astron.)
                    var gutenbergMatch = Regex.Match(domain, @"[<\(]([^>)]+)[>\)]");
                    if (gutenbergMatch.Success)
                    {
                        return gutenbergMatch.Groups[1].Value.Trim().TrimEnd('.');
                    }
                    return null;

                case "CENTURY21":
                    // Century21: (BrE.) - This is USAGE/LABEL, not domain
                    // Should NOT be stored in Domain column
                    return null;

                case "ENG_CHN":
                    // English-Chinese: may have domain markers like 〔医〕, 〔农〕
                    var chnMatch = Regex.Match(definition, @"〔([^〕]+)〕");
                    if (chnMatch.Success) return chnMatch.Groups[1].Value.Trim();
                    return null;

                default:
                    // Generic cleaner for unknown sources
                    return CleanDomainGeneric(domain, definition);
            }
        }
        private static string? CleanDomainGeneric(string domain, string definition)
        {
            // Remove Chinese characters
            domain = Regex.Replace(domain, @"[\u4e00-\u9fff]", "").Trim();

            // Remove any text after newline (definition contamination)
            if (domain.Contains('\n'))
            {
                domain = domain.Split('\n')[0].Trim();
            }

            // If it looks like definition text (contains "hours", "days", etc.), reject
            var definitionIndicators = new[] { "hours", "days", "weeks", "minutes", "seconds", "o'clock" };
            if (definitionIndicators.Any(ind => domain.Contains(ind, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            // Limit length
            return domain.Length <= 100 ? domain : null;
        }
    }
}