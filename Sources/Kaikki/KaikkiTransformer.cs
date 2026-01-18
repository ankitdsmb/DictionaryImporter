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
            var pronunciationInfo = ExtractPronunciationInfo(raw.Sounds);
            var audioUrl = ExtractPrimaryAudioUrl(raw.Sounds);

            foreach (var sense in raw.Senses.Where(s => s.Glosses != null && s.Glosses.Count > 0))
            {
                foreach (var gloss in sense.Glosses)
                {
                    if (string.IsNullOrWhiteSpace(gloss))
                        continue;

                    var fullDefinition = BuildFullDefinition(
                        gloss,
                        sense,
                        raw,
                        pronunciationInfo,
                        audioUrl,
                        senseNumber);

                    var entry = new DictionaryEntry
                    {
                        Word = raw.Word,
                        NormalizedWord = NormalizeWord(raw.Word),
                        PartOfSpeech = raw.Pos,
                        Definition = fullDefinition,
                        SenseNumber = senseNumber++,
                        SourceCode = "KAIKKI",
                        CreatedUtc = DateTime.UtcNow,
                        RawFragment = BuildRawFragment(raw, sense, gloss) // Store raw data for later parsing
                    };

                    yield return entry;
                }
            }
        }

        private string BuildFullDefinition(
            string gloss,
            KaikkiSense sense,
            KaikkiRawEntry raw,
            PronunciationInfo pronunciation,
            string? audioUrl,
            int senseNumber)
        {
            var parts = new List<string>();

            // Add Pronunciation section
            if (!string.IsNullOrWhiteSpace(pronunciation.Ipa) ||
                !string.IsNullOrWhiteSpace(pronunciation.Rhymes) ||
                !string.IsNullOrWhiteSpace(pronunciation.Enpr))
            {
                parts.Add("【Pronunciation】");
                if (!string.IsNullOrWhiteSpace(pronunciation.Ipa))
                    parts.Add($"IPA: {pronunciation.Ipa}");
                if (!string.IsNullOrWhiteSpace(pronunciation.Rhymes))
                    parts.Add($"Rhymes: {pronunciation.Rhymes}");
                if (!string.IsNullOrWhiteSpace(pronunciation.Enpr))
                    parts.Add($"Pronunciation: {pronunciation.Enpr}");
                if (!string.IsNullOrWhiteSpace(audioUrl))
                    parts.Add($"Audio: {audioUrl}");
            }

            // Add Part of Speech
            if (!string.IsNullOrWhiteSpace(raw.Pos))
            {
                parts.Add($"【POS】{NormalizePos(raw.Pos)}");
            }

            // Add Hyphenation if available
            if (raw.Hyphenations.Count > 0)
            {
                parts.Add($"【Hyphenation】{string.Join("; ", raw.Hyphenations.Take(2))}");
            }

            // Add Sense number
            parts.Add($"【Sense {senseNumber}】");

            // Add the main definition
            parts.Add(gloss);

            // Add Synonyms
            if (sense.Synonyms != null && sense.Synonyms.Count > 0)
            {
                parts.Add("【Synonyms】");
                var validSynonyms = sense.Synonyms
                    .Where(s => !string.IsNullOrWhiteSpace(s.Word))
                    .Take(5)
                    .ToList();

                foreach (var synonym in validSynonyms)
                {
                    var synonymText = synonym.Word!;
                    if (!string.IsNullOrWhiteSpace(synonym.Sense))
                    {
                        synonymText += $" ({synonym.Sense})";
                    }
                    parts.Add($"• {synonymText}");
                }
            }

            // Add Antonyms
            if (sense.Antonyms != null && sense.Antonyms.Count > 0)
            {
                parts.Add("【Antonyms】");
                var validAntonyms = sense.Antonyms
                    .Where(a => !string.IsNullOrWhiteSpace(a.Word))
                    .Take(3)
                    .ToList();

                foreach (var antonym in validAntonyms)
                {
                    parts.Add($"• {antonym.Word}");
                }
            }

            // Add Related words (cross-references)
            if (sense.Related != null && sense.Related.Count > 0)
            {
                parts.Add("【Related】");
                foreach (var related in sense.Related.Take(5))
                {
                    if (!string.IsNullOrWhiteSpace(related.Word))
                    {
                        var relatedText = related.Word;
                        if (!string.IsNullOrWhiteSpace(related.Type))
                        {
                            relatedText += $" [{related.Type}]";
                        }
                        parts.Add($"• {relatedText}");
                    }
                }
            }

            // Add Examples
            if (sense.Examples != null && sense.Examples.Count > 0)
            {
                parts.Add("【Examples】");
                var validExamples = sense.Examples
                    .Where(e => !string.IsNullOrWhiteSpace(e.Text))
                    .Take(3)
                    .ToList();

                foreach (var example in validExamples)
                {
                    var exampleText = CleanExampleText(example.Text!);
                    if (!string.IsNullOrWhiteSpace(example.Translation))
                    {
                        exampleText += $" | {CleanExampleText(example.Translation)}";
                    }
                    parts.Add($"• {exampleText}");
                }
            }

            // Add Forms (alternative forms, plural, etc.)
            if (sense.Forms != null && sense.Forms.Count > 0)
            {
                parts.Add("【Forms】");
                var validForms = sense.Forms
                    .Where(f => !string.IsNullOrWhiteSpace(f.Form))
                    .Take(5)
                    .ToList();

                foreach (var form in validForms)
                {
                    var formText = form.Form!;
                    if (form.Tags.Count > 0)
                    {
                        formText += $" [{string.Join(", ", form.Tags)}]";
                    }
                    parts.Add($"• {formText}");
                }
            }

            // Add Etymology
            if (!string.IsNullOrWhiteSpace(raw.EtymologyText))
            {
                parts.Add("【Etymology】");
                var cleanedEtymology = CleanEtymologyText(raw.EtymologyText);
                if (!string.IsNullOrWhiteSpace(cleanedEtymology))
                {
                    parts.Add(cleanedEtymology);
                }
            }

            // Add Categories/Topics
            if ((sense.Categories != null && sense.Categories.Count > 0) ||
                (sense.Topics != null && sense.Topics.Count > 0))
            {
                parts.Add("【Domain】");
                if (sense.Categories != null && sense.Categories.Count > 0)
                {
                    parts.Add($"Categories: {string.Join(", ", sense.Categories.Take(3))}");
                }
                if (sense.Topics != null && sense.Topics.Count > 0)
                {
                    parts.Add($"Topics: {string.Join(", ", sense.Topics.Take(3))}");
                }
            }

            // Add Tags
            if (sense.Tags != null && sense.Tags.Count > 0)
            {
                parts.Add($"【Tags】{string.Join(", ", sense.Tags.Take(3))}");
            }

            return string.Join("\n", parts);
        }

        private string BuildRawFragment(KaikkiRawEntry raw, KaikkiSense sense, string gloss)
        {
            // Store structured data as JSON for later parsing
            var rawData = new
            {
                word = raw.Word,
                pos = raw.Pos,
                sense = gloss,
                synonyms = sense.Synonyms?.Select(s => new { s.Word, s.Sense, s.Language }),
                antonyms = sense.Antonyms?.Select(a => new { a.Word, a.Sense }),
                related = sense.Related?.Select(r => new { r.Word, r.Type, r.Sense }),
                examples = sense.Examples?.Select(e => new { e.Text, e.Translation, e.Language }),
                forms = sense.Forms?.Select(f => new { f.Form, f.Tags }),
                categories = sense.Categories,
                topics = sense.Topics,
                tags = sense.Tags,
                etymology = raw.EtymologyText,
                translations = raw.Translations?.Select(t => new { t.Language, t.Code, t.Word, t.Sense })
            };

            return JsonSerializer.Serialize(rawData, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        private record PronunciationInfo(string? Ipa, string? Rhymes, string? Enpr);

        private PronunciationInfo ExtractPronunciationInfo(List<KaikkiSound> sounds)
        {
            if (sounds == null || sounds.Count == 0)
                return new PronunciationInfo(null, null, null);

            var primarySound = sounds.FirstOrDefault(s =>
                s.Tags != null &&
                (s.Tags.Contains("Received-Pronunciation") || s.Tags.Contains("UK")))
                ?? sounds.FirstOrDefault(s => s.Tags != null && s.Tags.Contains("US"))
                ?? sounds.FirstOrDefault();

            return new PronunciationInfo(
                primarySound?.Ipa,
                primarySound?.Rhymes,
                primarySound?.Enpr);
        }

        private string? ExtractPrimaryAudioUrl(List<KaikkiSound> sounds)
        {
            if (sounds == null || sounds.Count == 0)
                return null;

            var soundWithAudio = sounds.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.AudioUrl));
            return soundWithAudio?.AudioUrl;
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
                "phrase" => "phrase",
                "proverb" => "proverb",
                "prefix" => "prefix",
                "suffix" => "suffix",
                "abbreviation" or "abbr" => "abbreviation",
                _ => normalized
            };
        }

        private static string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            // Remove quotation marks if present
            example = example.Trim('"', '\'', '`', '«', '»', '「', '」', '『', '』');

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