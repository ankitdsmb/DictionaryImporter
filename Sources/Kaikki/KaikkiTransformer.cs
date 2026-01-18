namespace DictionaryImporter.Sources.Kaikki
{
    public sealed class KaikkiTransformer : IDataTransformer<KaikkiRawEntry>
    {
        private readonly ILogger<KaikkiTransformer> _logger;

        public KaikkiTransformer(ILogger<KaikkiTransformer> logger)
        {
            _logger = logger;
        }

        public IEnumerable<DictionaryEntry> Transform(KaikkiRawEntry raw)
        {
            if (raw == null || raw.Senses.Count == 0)
                yield break;

            var senseNumber = 1;
            var primaryIpa = ExtractPrimaryIpa(raw.Sounds);

            foreach (var sense in raw.Senses.Where(s => s.Glosses != null && s.Glosses.Count > 0))
            {
                foreach (var gloss in sense.Glosses)
                {
                    if (string.IsNullOrWhiteSpace(gloss))
                        continue;

                    var fullDefinition = BuildFullDefinition(gloss, sense, raw, primaryIpa);

                    yield return new DictionaryEntry
                    {
                        Word = raw.Word,
                        NormalizedWord = NormalizeWord(raw.Word),
                        PartOfSpeech = raw.Pos,
                        Definition = fullDefinition,
                        SenseNumber = senseNumber++,
                        SourceCode = "KAIKKI",
                        CreatedUtc = DateTime.UtcNow
                    };
                }
            }
        }

        private string BuildFullDefinition(string gloss, KaikkiSense sense, KaikkiRawEntry raw, string? ipa)
        {
            var parts = new List<string>();

            // Add IPA pronunciation if available
            if (!string.IsNullOrWhiteSpace(ipa))
            {
                parts.Add($"【Pronunciation】{ipa}");
            }

            // Add part of speech
            if (!string.IsNullOrWhiteSpace(raw.Pos))
            {
                parts.Add($"【POS】{NormalizePos(raw.Pos)}");
            }

            // Add the main definition
            parts.Add(gloss);

            // Add synonyms if available
            if (sense.Synonyms != null && sense.Synonyms.Count > 0)
            {
                parts.Add("【Synonyms】");
                var validSynonyms = sense.Synonyms
                    .Where(s => !string.IsNullOrWhiteSpace(s.Word))
                    .Take(5) // Limit to 5 synonyms
                    .ToList();

                foreach (var synonym in validSynonyms)
                {
                    var synonymText = synonym.Word;
                    if (!string.IsNullOrWhiteSpace(synonym.Sense))
                    {
                        synonymText += $" ({synonym.Sense})";
                    }
                    parts.Add($"• {synonymText}");
                }
            }

            // Add examples if available
            if (sense.Examples != null && sense.Examples.Count > 0)
            {
                parts.Add("【Examples】");
                var validExamples = sense.Examples
                    .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                    .Take(3)
                    .ToList();

                foreach (var example in validExamples)
                {
                    var cleanedExample = CleanExampleText(example.Text!);
                    if (!string.IsNullOrWhiteSpace(cleanedExample))
                    {
                        parts.Add($"• {cleanedExample}");
                    }
                }
            }

            // Add etymology if available
            if (!string.IsNullOrWhiteSpace(raw.EtymologyText))
            {
                parts.Add("【Etymology】");
                var cleanedEtymology = CleanEtymologyText(raw.EtymologyText);
                if (!string.IsNullOrWhiteSpace(cleanedEtymology))
                {
                    parts.Add(cleanedEtymology);
                }
            }

            // Add categories/topics if available
            if ((sense.Categories != null && sense.Categories.Count > 0) ||
                (sense.Topics != null && sense.Topics.Count > 0))
            {
                var domainInfo = new List<string>();
                if (sense.Categories != null && sense.Categories.Count > 0)
                {
                    domainInfo.Add($"Categories: {string.Join(", ", sense.Categories)}");
                }
                if (sense.Topics != null && sense.Topics.Count > 0)
                {
                    domainInfo.Add($"Topics: {string.Join(", ", sense.Topics)}");
                }
                if (domainInfo.Count > 0)
                {
                    parts.Add($"【Domain】{string.Join("; ", domainInfo)}");
                }
            }

            return string.Join("\n", parts);
        }

        private string? ExtractPrimaryIpa(List<KaikkiSound> sounds)
        {
            if (sounds == null || sounds.Count == 0)
                return null;

            // Prefer UK pronunciation, then US, then any IPA
            var ukIpa = sounds.FirstOrDefault(s =>
                s.Tags != null &&
                (s.Tags.Contains("Received-Pronunciation") || s.Tags.Contains("UK")))?.Ipa;

            if (!string.IsNullOrWhiteSpace(ukIpa))
                return ukIpa;

            var usIpa = sounds.FirstOrDefault(s =>
                s.Tags != null && s.Tags.Contains("US"))?.Ipa;

            if (!string.IsNullOrWhiteSpace(usIpa))
                return usIpa;

            return sounds.FirstOrDefault()?.Ipa;
        }

        private static string NormalizeWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            return word.ToLowerInvariant()
                .Replace("(", "")
                .Replace(")", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("{", "")
                .Replace("}", "")
                .Trim();
        }

        private static string NormalizePos(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos))
                return "unk";

            var normalized = pos.Trim().ToLowerInvariant();

            return normalized switch
            {
                "n" or "noun" => "noun",
                "v" or "verb" => "verb",
                "adj" or "adjective" => "adj",
                "adv" or "adverb" => "adv",
                "prep" or "preposition" => "preposition",
                "pron" or "pronoun" => "pronoun",
                "conj" or "conjunction" => "conjunction",
                "interj" or "interjection" => "exclamation",
                "num" or "numeral" => "numeral",
                "det" or "determiner" => "determiner",
                "part" or "particle" => "particle",
                _ => normalized
            };
        }

        private static string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            // Remove quotation marks if present
            example = example.Trim('"', '\'', '`', '«', '»', '“', '”');

            // Ensure proper ending punctuation
            if (!example.EndsWith(".") && !example.EndsWith("!") && !example.EndsWith("?"))
            {
                example += ".";
            }

            return example.Trim();
        }

        private static string CleanEtymologyText(string etymology)
        {
            if (string.IsNullOrWhiteSpace(etymology))
                return string.Empty;

            // Extract only the plain text etymology, remove tree structure
            var lines = etymology.Split('\n')
                .Where(line => !line.StartsWith("Proto-") &&
                              !line.StartsWith("Etymology tree") &&
                              !line.Contains("der.") &&
                              !line.Contains("lbor."))
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join(" ", lines.Take(3)); // Limit to first 3 relevant lines
        }
    }
}