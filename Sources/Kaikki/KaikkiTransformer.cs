using DictionaryImporter.Sources.Kaikki.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Kaikki;

public sealed class KaikkiTransformer : IDataTransformer<KaikkiRawEntry>
{
    private readonly ILogger<KaikkiTransformer> _logger;

    public KaikkiTransformer(ILogger<KaikkiTransformer> logger)
    {
        _logger = logger;
    }

    public IEnumerable<DictionaryEntry> Transform(KaikkiRawEntry raw)
    {
        if (raw == null)
        {
            _logger.LogWarning("Received null KaikkiRawEntry");
            yield break;
        }

        if (raw.Senses == null || raw.Senses.Count == 0)
        {
            _logger.LogDebug("No senses found for word: {Word}", raw.Word);
            yield break;
        }

        var normalizedWord = NormalizeWord(raw.Word);
        if (string.IsNullOrWhiteSpace(normalizedWord))
        {
            _logger.LogDebug("Empty normalized word for: {Word}", raw.Word);
            yield break;
        }

        // Process each sense as a separate DictionaryEntry
        var senseNumber = 1;

        foreach (var sense in raw.Senses)
        {
            if (sense.Glosses == null || sense.Glosses.Count == 0)
            {
                _logger.LogDebug("Empty gloss for word: {Word}, sense: {SenseNumber}", raw.Word, senseNumber);
                senseNumber++;
                continue;
            }

            // Build the definition string
            var definition = BuildDefinition(raw, sense, senseNumber);

            // Build raw fragment (JSON string for parsing)
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
        var sb = new StringBuilder();

        // Add part of speech
        if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
        {
            sb.AppendLine($"【POS】{NormalizePartOfSpeech(raw.PartOfSpeech)}");
        }

        // Add IPA pronunciation
        var ipa = ExtractIpa(raw);
        if (!string.IsNullOrWhiteSpace(ipa))
        {
            sb.AppendLine($"【Pronunciation】{ipa}");
        }

        // Add etymology if available
        if (!string.IsNullOrWhiteSpace(raw.EtymologyText))
        {
            sb.AppendLine($"【Etymology】{CleanText(raw.EtymologyText)}");
        }

        // Add sense number if multiple senses
        if (raw.Senses.Count > 1)
        {
            sb.AppendLine($"【Sense {senseNumber}】");
        }

        // Add glosses/definitions
        var glossText = string.Join("; ", sense.Glosses.Select(CleanText));
        sb.AppendLine(glossText);

        // Add categories/topics
        if (sense.Categories != null && sense.Categories.Count > 0)
        {
            var categories = string.Join(", ", sense.Categories.Select(CleanText));
            sb.AppendLine($"【Categories】{categories}");
        }

        if (sense.Topics != null && sense.Topics.Count > 0)
        {
            var topics = string.Join(", ", sense.Topics.Select(CleanText));
            sb.AppendLine($"【Topics】{topics}");
        }

        // Add examples
        if (sense.Examples != null && sense.Examples.Count > 0)
        {
            sb.AppendLine("【Examples】");
            foreach (var example in sense.Examples.Take(5)) // Limit to 5 examples
            {
                sb.AppendLine($"• {CleanText(example.Text)}");
            }
        }

        // Add tags
        if (sense.Tags != null && sense.Tags.Count > 0)
        {
            var tags = string.Join(", ", sense.Tags.Select(CleanText));
            sb.AppendLine($"【Tags】{tags}");
        }

        // Add synonyms
        if (raw.Synonyms != null && raw.Synonyms.Count > 0)
        {
            var synonyms = string.Join(", ", raw.Synonyms.Select(s => s.Word).Distinct());
            sb.AppendLine($"【Synonyms】{synonyms}");
        }

        // Add antonyms
        if (raw.Antonyms != null && raw.Antonyms.Count > 0)
        {
            var antonyms = string.Join(", ", raw.Antonyms.Select(s => s.Word).Distinct());
            sb.AppendLine($"【Antonyms】{antonyms}");
        }

        // Add derived forms
        if (raw.Derived != null && raw.Derived.Count > 0)
        {
            var derived = string.Join(", ", raw.Derived.Select(d => d.Word).Distinct());
            sb.AppendLine($"【Derived】{derived}");
        }

        return sb.ToString().Trim();
    }

    private string BuildRawFragment(KaikkiRawEntry raw, KaikkiSense sense, int senseNumber)
    {
        // Create a simplified object for parsing
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

        // Try to find IPA in sounds
        foreach (var sound in raw.Sounds)
        {
            if (!string.IsNullOrWhiteSpace(sound.Ipa))
            {
                var ipa = sound.Ipa.Trim();
                // Ensure it's properly formatted
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

        // Remove any markup
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
            _ => normalized.Length > 20 ? null : normalized // Too long, probably not a valid POS
        };
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove Wiki markup
        text = Regex.Replace(text, @"\{\{.*?\}\}", string.Empty);
        text = Regex.Replace(text, @"\[\[.*?\]\]", string.Empty);
        text = Regex.Replace(text, @"'''(.*?)'''", "$1");
        text = Regex.Replace(text, @"''(.*?)''", "$1");

        // Remove HTML tags
        text = Regex.Replace(text, @"<.*?>", string.Empty);

        // Remove references
        text = Regex.Replace(text, @"&lt;.*?&gt;", string.Empty);
        text = Regex.Replace(text, @"&amp;", "&");

        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }
}