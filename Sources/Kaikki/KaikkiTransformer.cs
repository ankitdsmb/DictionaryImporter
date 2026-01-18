using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Kaikki.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DictionaryImporter.Sources.Kaikki
{
    public sealed class KaikkiTransformer(ILogger<KaikkiTransformer> logger) : IDataTransformer<KaikkiRawEntry>
    {
        public IEnumerable<DictionaryEntry> Transform(KaikkiRawEntry raw)
        {
            if (raw == null)
            {
                logger.LogWarning("Received null KaikkiRawEntry");
                yield break;
            }

            if (raw.Senses == null || raw.Senses.Count == 0)
            {
                logger.LogDebug("No senses found for word: {Word}", raw.Word);
                yield break;
            }

            var normalizedWord = NormalizeWord(raw.Word);
            if (string.IsNullOrWhiteSpace(normalizedWord))
            {
                logger.LogDebug("Empty normalized word for: {Word}", raw.Word);
                yield break;
            }

            var senseNumber = 1;

            foreach (var sense in raw.Senses)
            {
                if (sense.Glosses == null || sense.Glosses.Count == 0)
                {
                    logger.LogDebug("Empty gloss for word: {Word}, sense: {SenseNumber}", raw.Word, senseNumber);
                    senseNumber++;
                    continue;
                }

                var definition = BuildDefinition(raw, sense, senseNumber);

                var rawFragment = BuildRawFragment(raw, sense, senseNumber);

                var entry = new DictionaryEntry
                {
                    Word = raw.Word,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = NormalizePartOfSpeech(raw.PartOfSpeech),
                    Definition = definition,
                    Etymology = raw.EtymologyText,
                    SenseNumber = senseNumber,
                    SourceCode = "KAIKKI",
                    CreatedUtc = DateTime.UtcNow
                };

                yield return entry;
                senseNumber++;
            }
        }

        private string BuildDefinition(KaikkiRawEntry raw, KaikkiSense sense, int senseNumber)
        {
            var sections = new List<string>();

            // Collect all sections without numbering
            if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
            {
                sections.Add($"【POS】{NormalizePartOfSpeech(raw.PartOfSpeech)}");
            }

            var ipa = ExtractIpa(raw);
            if (!string.IsNullOrWhiteSpace(ipa))
            {
                sections.Add($"【Pronunciation】{ipa}");
            }

            if (!string.IsNullOrWhiteSpace(raw.EtymologyText))
            {
                sections.Add($"【Etymology】{CleanText(raw.EtymologyText)}");
            }

            if (raw.Senses.Count > 1)
            {
                sections.Add($"【Sense {senseNumber}】");
            }

            var glossText = string.Join("; ", sense.Glosses.Select(CleanText));
            if (!string.IsNullOrWhiteSpace(glossText))
            {
                sections.Add(glossText);
            }

            var categories = sense.Categories.ToStringList();
            if (categories.Count > 0)
            {
                var categoriesText = string.Join(", ", categories.Select(CleanText));
                if (!string.IsNullOrWhiteSpace(categoriesText))
                {
                    sections.Add($"【Categories】{categoriesText}");
                }
            }

            var topics = sense.Topics.ToStringList();
            if (topics.Count > 0)
            {
                var topicsText = string.Join(", ", topics.Select(CleanText));
                if (!string.IsNullOrWhiteSpace(topicsText))
                {
                    sections.Add($"【Topics】{topicsText}");
                }
            }

            if (sense.Examples != null && sense.Examples.Count > 0)
            {
                var exampleText = new StringBuilder("【Examples】");
                foreach (var example in sense.Examples.Take(5))
                {
                    var cleaned = CleanText(example.Text);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        exampleText.Append($"\n• {cleaned}");
                    }
                }
                sections.Add(exampleText.ToString());
            }

            var tags = sense.Tags.ToStringList();
            if (tags.Count > 0)
            {
                var tagsText = string.Join(", ", tags.Select(CleanText));
                if (!string.IsNullOrWhiteSpace(tagsText))
                {
                    sections.Add($"【Tags】{tagsText}");
                }
            }

            if (raw.Synonyms != null && raw.Synonyms.Count > 0)
            {
                var synonyms = string.Join(", ", raw.Synonyms.Select(s => s.Word).Distinct());
                if (!string.IsNullOrWhiteSpace(synonyms))
                {
                    sections.Add($"【Synonyms】{synonyms}");
                }
            }

            if (raw.Antonyms != null && raw.Antonyms.Count > 0)
            {
                var antonyms = string.Join(", ", raw.Antonyms.Select(s => s.Word).Distinct());
                if (!string.IsNullOrWhiteSpace(antonyms))
                {
                    sections.Add($"【Antonyms】{antonyms}");
                }
            }

            if (raw.Derived != null && raw.Derived.Count > 0)
            {
                var derived = string.Join(", ", raw.Derived.Select(d => d.Word).Distinct());
                if (!string.IsNullOrWhiteSpace(derived))
                {
                    sections.Add($"【Derived】{derived}");
                }
            }

            // Apply numbering ONLY HERE, at the very end
            var sb = new StringBuilder();
            for (int i = 0; i < sections.Count; i++)
            {
                sb.AppendLine($"{i + 1}) {sections[i]}");
            }

            return sb.ToString().Trim();
        }

        private string BuildRawFragment(KaikkiRawEntry raw, KaikkiSense sense, int senseNumber)
        {
            var parsingData = new
            {
                Word = raw.Word,
                PartOfSpeech = raw.PartOfSpeech,
                Sense = sense,
                Sounds = raw.Sounds,
                Etymology = raw.EtymologyText,
                Synonyms = raw.Synonyms,
                Antonyms = raw.Antonyms,
                Derived = raw.Derived,
                SenseNumber = senseNumber
            };

            return JsonConvert.SerializeObject(parsingData, Formatting.Indented);
        }

        private string? ExtractIpa(KaikkiRawEntry raw)
        {
            if (raw.Sounds == null || raw.Sounds.Count == 0)
                return null;

            foreach (var sound in raw.Sounds)
            {
                if (!string.IsNullOrWhiteSpace(sound.Ipa))
                {
                    var ipa = sound.Ipa.Trim();
                    if (!ipa.StartsWith("/") && !ipa.StartsWith("["))
                        ipa = "/" + ipa + "/";
                    return ipa;
                }
            }

            return null;
        }

        private static string NormalizeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            return word.ToLowerInvariant()
                .Replace("★", "")
                .Replace("☆", "")
                .Replace("●", "")
                .Replace("○", "")
                .Replace("▶", "")
                .Trim();
        }

        private static string? NormalizePartOfSpeech(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos))
                return null;

            var normalized = pos.Trim().ToLowerInvariant();

            normalized = Regex.Replace(normalized, @"\{\{.*?\}\}", string.Empty);

            return normalized switch
            {
                "noun" => "noun",
                "verb" => "verb",
                "adj" => "adj",
                "adjective" => "adj",
                "adv" => "adv",
                "adverb" => "adv",
                "prep" => "preposition",
                "preposition" => "preposition",
                "pron" => "pronoun",
                "pronoun" => "pronoun",
                "conj" => "conjunction",
                "conjunction" => "conjunction",
                "interj" => "exclamation",
                "interjection" => "exclamation",
                "exclamation" => "exclamation",
                "abbr" => "abbreviation",
                "abbreviation" => "abbreviation",
                "pref" => "prefix",
                "prefix" => "prefix",
                "suf" => "suffix",
                "suffix" => "suffix",
                "num" => "numeral",
                "numeral" => "numeral",
                "art" => "determiner",
                "article" => "determiner",
                "determiner" => "determiner",
                "aux" => "auxiliary",
                "auxiliary" => "auxiliary",
                "modal" => "modal",
                "part" => "particle",
                "particle" => "particle",
                "phrase" => "phrase",
                "symbol" => "symbol",
                "character" => "character",
                "letter" => "letter",
                _ => normalized.Length > 20 ? null : normalized
            };
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove wiki markup
            text = Regex.Replace(text, @"\{\{.*?\}\}", string.Empty);
            text = Regex.Replace(text, @"\[\[.*?\]\]", string.Empty);
            text = Regex.Replace(text, @"'''(.*?)'''", "$1");
            text = Regex.Replace(text, @"''(.*?)''", "$1");

            // Remove HTML tags
            text = Regex.Replace(text, @"<.*?>", string.Empty);

            // Decode HTML entities
            text = Regex.Replace(text, @"&lt;", "<");
            text = Regex.Replace(text, @"&gt;", ">");
            text = Regex.Replace(text, @"&amp;", "&");
            text = Regex.Replace(text, @"&quot;", "\"");
            text = Regex.Replace(text, @"&apos;", "'");

            // Remove leftover wiki/markup characters
            text = Regex.Replace(text, @"\|\|", " ");
            text = Regex.Replace(text, @"\{\||\|\}", " ");
            text = Regex.Replace(text, @"\{\{|\}\}", " ");

            // Remove any existing numbering at the start (e.g., "1) ", "2) ")
            text = Regex.Replace(text, @"^\d+\)\s*", "");

            // Remove bullet points at the beginning
            text = Regex.Replace(text, @"^[•\-\*]\s*", "");

            // Normalize whitespace
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }
    }
}