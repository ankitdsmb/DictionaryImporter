using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;

namespace DictionaryImporter.Sources.EnglishChinese.Parsing
{
    public sealed class EnglishChineseDefinitionParser : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "ENG_CHN";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            if (entry == null)
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            // ✅ Use RawFragment if available, otherwise use Definition
            var rawDefinition = !string.IsNullOrWhiteSpace(entry.RawFragment)
                ? entry.RawFragment
                : entry.Definition;

            if (string.IsNullOrWhiteSpace(rawDefinition))
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            List<ParsedDefinition> parsedDefinitions;

            try
            {
                // Parse based on actual ENG_CHN format discovered
                parsedDefinitions = ParseEngChnDefinition(rawDefinition, entry.Word);
            }
            catch (Exception ex)
            {
                // Log error and return fallback
                // Note: In production, you'd want to inject ILogger
                parsedDefinitions = new List<ParsedDefinition>
                {
                    SourceDataHelper.CreateFallbackParsedDefinition(entry)
                };
            }

            if (parsedDefinitions.Count == 0)
            {
                // Fallback: return the raw definition
                yield return CreateParsedDefinition(entry, rawDefinition, rawDefinition);
            }
            else
            {
                foreach (var parsedDef in parsedDefinitions)
                {
                    yield return parsedDef;
                }
            }
        }

        private List<ParsedDefinition> ParseEngChnDefinition(string definition, string word)
        {
            var results = new List<ParsedDefinition>();

            if (string.IsNullOrWhiteSpace(definition))
                return results;

            // Clean the definition
            var cleaned = CleanEngChnDefinition(definition);

            // Extract components
            var components = ExtractEngChnComponents(cleaned);

            // Create parsed definition
            var parsedDef = new ParsedDefinition
            {
                MeaningTitle = word ?? "unnamed sense",
                Definition = components.ChineseDefinition ?? cleaned, // Fallback to full definition
                RawFragment = definition,
                SenseNumber = 1,
                Domain = null,
                UsageLabel = components.PartOfSpeech,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };

            // Add pronunciation to definition if present
            if (!string.IsNullOrWhiteSpace(components.Pronunciation))
            {
                parsedDef.Definition = $"[Pronunciation: {components.Pronunciation}] " + parsedDef.Definition;
            }

            results.Add(parsedDef);
            return results;
        }

        private (string ChineseDefinition, string PartOfSpeech, string Pronunciation) ExtractEngChnComponents(string definition)
        {
            string chineseDefinition = null;
            string partOfSpeech = null;
            string pronunciation = null;

            // Extract pronunciation (IPA) first
            var pronunciationMatch = Regex.Match(definition, @"/([^/]+)/");
            if (pronunciationMatch.Success)
            {
                pronunciation = pronunciationMatch.Value;
            }

            // Extract part of speech
            var posMatch = Regex.Match(definition, @"\b(n|a|v|adj|adv|abbr)\.", RegexOptions.IgnoreCase);
            if (posMatch.Success)
            {
                var posAbbr = posMatch.Groups[1].Value.ToLower();
                partOfSpeech = posAbbr switch
                {
                    "n" => "noun",
                    "v" => "verb",
                    "a" => "adj",
                    "adj" => "adj",
                    "adv" => "adv",
                    "abbr" => "abbreviation",
                    _ => null
                };

                // Extract Chinese definition: everything after part of speech marker
                var afterPos = definition.Substring(posMatch.Index + posMatch.Length).Trim();

                // Find where Chinese definition starts (skip any remaining English)
                chineseDefinition = ExtractCompleteChineseDefinition(afterPos);
            }
            else
            {
                // No part of speech marker, try to extract Chinese directly
                chineseDefinition = ExtractCompleteChineseDefinition(definition);
            }

            return (chineseDefinition, partOfSpeech, pronunciation);
        }

        private string ExtractCompleteChineseDefinition(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Find the start of the Chinese definition
            // It could start with numbers, Chinese characters, or Chinese punctuation
            var startIndex = 0;
            var foundStart = false;

            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var charCode = (int)c;

                // Accept: Chinese characters, Chinese punctuation, digits, spaces, hyphens
                var isChineseChar = (charCode >= 19968 && charCode <= 40959) ||
                                   (charCode >= 13312 && charCode <= 19903);
                var isChinesePunct = c == '。' || c == '；' || c == '，' || c == '、' ||
                                    c == '〔' || c == '〕' || c == '【' || c == '】' ||
                                    c == '（' || c == '）' || c == '《' || c == '》';

                if (isChineseChar || isChinesePunct || char.IsDigit(c) || c == ' ' || c == '-')
                {
                    if (!foundStart)
                    {
                        startIndex = i;
                        foundStart = true;
                    }
                }
                else if (foundStart && char.IsLetter(c) && (c < 'A' || c > 'Z') && (c < 'a' || c > 'z'))
                {
                    // Found a non-English letter after Chinese start, continue
                    continue;
                }
                else if (foundStart)
                {
                    // Found something else (like '[ < etymology'), stop here
                    break;
                }
            }

            if (foundStart)
            {
                var chineseText = text.Substring(startIndex).Trim();

                // Remove any trailing etymology in brackets
                var bracketIndex = chineseText.IndexOf('[');
                if (bracketIndex > 0)
                {
                    chineseText = chineseText.Substring(0, bracketIndex).Trim();
                }

                return chineseText;
            }

            return null;
        }

        private string CleanEngChnDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            var cleaned = definition;

            // Remove any remaining HTML tags
            cleaned = Regex.Replace(cleaned, @"<[^>]+>", " ");

            // Normalize whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        private ParsedDefinition CreateParsedDefinition(DictionaryEntry entry, string definition, string rawFragment)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = definition,
                RawFragment = rawFragment,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }
    }
}