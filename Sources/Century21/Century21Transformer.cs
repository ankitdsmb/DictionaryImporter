namespace DictionaryImporter.Sources.Century21
{
    public sealed class Century21Transformer(ILogger<Century21Transformer> logger) : IDataTransformer<Century21RawEntry>
    {
        public IEnumerable<DictionaryEntry> Transform(Century21RawEntry raw)
        {
            if (raw == null)
                yield break;

            var senseNumber = 1;

            var mainDefinition = BuildDefinition(raw);
            yield return new DictionaryEntry
            {
                Word = raw.Headword,
                NormalizedWord = NormalizeWord(raw.Headword),
                PartOfSpeech = NormalizePartOfSpeech(raw.PartOfSpeech),
                Definition = mainDefinition,
                SenseNumber = senseNumber++,
                SourceCode = "COUNTRY21",
                CreatedUtc = DateTime.UtcNow
            };

            foreach (var variant in raw.Variants)
            {
                var variantDefinition = BuildVariantDefinition(variant);
                yield return new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = NormalizeWord(raw.Headword),
                    PartOfSpeech = NormalizePartOfSpeech(variant.PartOfSpeech),
                    Definition = variantDefinition,
                    SenseNumber = senseNumber++,
                    SourceCode = "COUNTRY21",
                    CreatedUtc = DateTime.UtcNow
                };
            }

            foreach (var idiom in raw.Idioms)
            {
                var idiomDefinition = BuildIdiomDefinition(idiom);
                yield return new DictionaryEntry
                {
                    Word = idiom.Headword,
                    NormalizedWord = NormalizeWord(idiom.Headword),
                    PartOfSpeech = "phrase",
                    Definition = idiomDefinition,
                    SenseNumber = 1,
                    SourceCode = "COUNTRY21",
                    CreatedUtc = DateTime.UtcNow
                };
            }
        }

        private static string BuildDefinition(Century21RawEntry raw)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(raw.Phonetics))
                parts.Add($"【Pronunciation】{raw.Phonetics}");

            if (!string.IsNullOrWhiteSpace(raw.GrammarInfo))
                parts.Add($"【Grammar】{raw.GrammarInfo}");

            parts.Add(raw.Definition);

            if (raw.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in raw.Examples)
                {
                    var exampleText = example.English;
                    if (!string.IsNullOrWhiteSpace(example.Chinese))
                        exampleText += $" ({example.Chinese})";
                    parts.Add($"• {exampleText}");
                }
            }

            return string.Join("\n", parts);
        }

        private static string BuildVariantDefinition(Country21Variant variant)
        {
            var parts = new List<string>();

            parts.Add(variant.Definition);

            if (variant.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in variant.Examples)
                {
                    var exampleText = example.English;
                    if (!string.IsNullOrWhiteSpace(example.Chinese))
                        exampleText += $" ({example.Chinese})";
                    parts.Add($"• {exampleText}");
                }
            }

            return string.Join("\n", parts);
        }

        private static string BuildIdiomDefinition(Country21Idiom idiom)
        {
            var parts = new List<string>();

            parts.Add(idiom.Definition);

            if (idiom.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in idiom.Examples)
                {
                    var exampleText = example.English;
                    if (!string.IsNullOrWhiteSpace(example.Chinese))
                        exampleText += $" ({example.Chinese})";
                    parts.Add($"• {exampleText}");
                }
            }

            return string.Join("\n", parts);
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

            return normalized switch
            {
                "n." => "noun",
                "v." => "verb",
                "vi." => "verb",
                "vt." => "verb",
                "adj." => "adj",
                "adv." => "adv",
                "prep." => "preposition",
                "pron." => "pronoun",
                "conj." => "conjunction",
                "interj." => "exclamation",
                "abbr." => "abbreviation",
                "pref." => "prefix",
                "suf." => "suffix",
                _ => normalized.EndsWith('.') ? normalized[..^1] : normalized
            };
        }

        private static string BuildDefinition1(Century21RawEntry raw)
        {
            var parts = new List<string>();

            var rawFragment = BuildRawFragment(raw);

            if (!string.IsNullOrWhiteSpace(raw.Phonetics))
                parts.Add($"【Pronunciation】{raw.Phonetics}");

            if (!string.IsNullOrWhiteSpace(raw.GrammarInfo))
                parts.Add($"【Grammar】{raw.GrammarInfo}");

            parts.Add(raw.Definition);

            if (raw.Examples.Any())
            {
                parts.Add("【Examples】");
                foreach (var example in raw.Examples)
                {
                    parts.Add($"• {example.English}");
                }
            }

            return string.Join("\n", parts);
        }

        private static string BuildRawFragment(Century21RawEntry raw)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<div class=\"word_block\">");
            sb.AppendLine($"  <div class=\"basic_def\">");
            sb.AppendLine($"    <div class=\"item\">");
            sb.AppendLine($"      <span class=\"headword\">{raw.Headword}</span>");

            if (!string.IsNullOrWhiteSpace(raw.Phonetics))
                sb.AppendLine($"      <div class=\"sound_notation\"><span class=\"phonetics\">{raw.Phonetics}</span></div>");

            if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
                sb.AppendLine($"      <span class=\"pos\">{raw.PartOfSpeech}</span>");

            sb.AppendLine($"      <span class=\"definition\">{raw.Definition}</span>");
            sb.AppendLine($"    </div>");
            sb.AppendLine($"  </div>");

            foreach (var variant in raw.Variants)
            {
                sb.AppendLine($"  <div class=\"variant\">");
                sb.AppendLine($"    <div class=\"item\">");
                if (!string.IsNullOrWhiteSpace(variant.PartOfSpeech))
                    sb.AppendLine($"      <span class=\"pos\">{variant.PartOfSpeech}</span>");
                sb.AppendLine($"      <span class=\"definition\">{variant.Definition}</span>");
                sb.AppendLine($"    </div>");
                sb.AppendLine($"  </div>");
            }

            foreach (var idiom in raw.Idioms)
            {
                sb.AppendLine($"  <div class=\"idiom\">");
                sb.AppendLine($"    <div class=\"item\">");
                sb.AppendLine($"      <span class=\"headword\">{idiom.Headword}</span>");
                sb.AppendLine($"      <span class=\"definition\">{idiom.Definition}</span>");
                sb.AppendLine($"    </div>");
                sb.AppendLine($"  </div>");
            }

            sb.AppendLine($"</div>");

            return sb.ToString();
        }
    }
}