using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Century21
{
    public sealed class Century21Transformer(ILogger<Century21Transformer> logger)
        : IDataTransformer<Century21RawEntry>
    {
        private const string SourceCode = "CENTURY21";

        public IEnumerable<DictionaryEntry> Transform(Century21RawEntry? raw)
        {
            if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, logger))
                yield break;

            if (raw == null)
                yield break;

            foreach (var entry in ProcessCentury21Entry(raw))
                yield return entry;
        }

        private IEnumerable<DictionaryEntry> ProcessCentury21Entry(Century21RawEntry raw)
        {
            var entries = new List<DictionaryEntry>();

            try
            {
                var senseNumber = 1;

                var normalizedHeadword = SourceDataHelper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);
                var mainDefinition = BuildDefinition(raw);

                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedHeadword,
                    PartOfSpeech = SourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech),
                    Definition = mainDefinition,
                    // CRITICAL FIX: Preserve original structure, not just definition
                    RawFragment = BuildRawFragment(raw), // Create a method to preserve original
                    SenseNumber = senseNumber++,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow
                });

                foreach (var variant in raw.Variants)
                {
                    var variantDefinition = BuildVariantDefinition(variant);

                    entries.Add(new DictionaryEntry
                    {
                        Word = raw.Headword,
                        NormalizedWord = normalizedHeadword,
                        PartOfSpeech = SourceDataHelper.NormalizePartOfSpeech(variant.PartOfSpeech),
                        Definition = variantDefinition,
                        RawFragment = variantDefinition, // FIX
                        SenseNumber = senseNumber++,
                        SourceCode = SourceCode,
                        CreatedUtc = DateTime.UtcNow
                    });
                }

                foreach (var idiom in raw.Idioms)
                {
                    var normalizedIdiomWord = SourceDataHelper.NormalizeWord(idiom.Headword);
                    var idiomDefinition = BuildIdiomDefinition(idiom);

                    entries.Add(new DictionaryEntry
                    {
                        Word = idiom.Headword,
                        NormalizedWord = normalizedIdiomWord,
                        PartOfSpeech = "phrase",
                        Definition = idiomDefinition,
                        RawFragment = idiomDefinition, // FIX
                        SenseNumber = 1,
                        SourceCode = SourceCode,
                        CreatedUtc = DateTime.UtcNow
                    });
                }

                SourceDataHelper.LogProgress(logger, SourceCode, SourceDataHelper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                SourceDataHelper.HandleError(logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }

        // Add method to preserve original structure:
        private static string BuildRawFragment(Century21RawEntry raw)
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
                parts.AddRange(raw.Examples.Select(e => $"• {e.English}" +
                                                        (!string.IsNullOrEmpty(e.Chinese) ? $" ({e.Chinese})" : "")));
            }

            return string.Join("\n", parts);
        }

        private static string BuildDefinition(Century21RawEntry raw)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(raw.Phonetics))
                parts.Add($"【Pronunciation】{raw.Phonetics}");

            if (!string.IsNullOrWhiteSpace(raw.GrammarInfo))
                parts.Add($"【Grammar】{raw.GrammarInfo}");

            // ✅ FIX: Use source-aware normalization
            var definition = SourceDataHelper.NormalizeDefinitionForSource(
                raw.Definition, SourceCode);
            parts.Add(definition);

            AddExamples(parts, raw.Examples);

            return string.Join("\n", parts);
        }

        private static string BuildVariantDefinition(Country21Variant variant)
        {
            var parts = new List<string> { variant.Definition };
            AddExamples(parts, variant.Examples);
            return string.Join("\n", parts);
        }

        private static string BuildIdiomDefinition(Country21Idiom idiom)
        {
            var parts = new List<string> { idiom.Definition };
            AddExamples(parts, idiom.Examples);
            return string.Join("\n", parts);
        }

        private static void AddExamples(List<string> parts, IEnumerable<Country21Example> examples)
        {
            var country21Examples = examples as Country21Example[] ?? examples.ToArray();
            if (!country21Examples.Any())
                return;

            parts.Add("【Examples】");

            foreach (var example in country21Examples)
            {
                var exampleText = example.English;

                if (!string.IsNullOrWhiteSpace(example.Chinese))
                    exampleText += $" ({example.Chinese})";

                parts.Add($"• {exampleText}");
            }
        }
    }
}