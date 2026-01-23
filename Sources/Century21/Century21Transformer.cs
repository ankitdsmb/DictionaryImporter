using DictionaryImporter.Common;
using HtmlAgilityPack;
using System.Net;

namespace DictionaryImporter.Sources.Century21
{
    public sealed class Century21Transformer(ILogger<Century21Transformer> logger)
        : IDataTransformer<Century21RawEntry>
    {
        private const string SourceCode = "CENTURY21";

        public IEnumerable<DictionaryEntry> Transform(Century21RawEntry? raw)
        {
            if (!Helper.ShouldContinueProcessing(SourceCode, logger))
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

                var normalizedHeadword = Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

                // ✅ Build proper RawFragment (HTML structure for parser)
                var rawFragment = BuildHtmlRawFragment(raw);

                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedHeadword,
                    PartOfSpeech = Helper.NormalizePartOfSpeech(raw.PartOfSpeech),
                    Definition = BuildDefinition(raw),
                    RawFragment = rawFragment,
                    SenseNumber = senseNumber++,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow
                });

                // Add variants with proper RawFragment
                foreach (var variant in raw.Variants)
                {
                    var variantRawFragment = BuildVariantHtmlFragment(raw.Headword, variant);

                    entries.Add(new DictionaryEntry
                    {
                        Word = raw.Headword,
                        NormalizedWord = normalizedHeadword,
                        PartOfSpeech = Helper.NormalizePartOfSpeech(variant.PartOfSpeech),
                        Definition = BuildVariantDefinition(variant),
                        RawFragment = variantRawFragment,
                        SenseNumber = senseNumber++,
                        SourceCode = SourceCode,
                        CreatedUtc = DateTime.UtcNow
                    });
                }

                // Add idioms with proper RawFragment
                foreach (var idiom in raw.Idioms)
                {
                    var normalizedIdiomWord = Helper.NormalizeWord(idiom.Headword);
                    var idiomRawFragment = BuildIdiomHtmlFragment(idiom);

                    entries.Add(new DictionaryEntry
                    {
                        Word = idiom.Headword,
                        NormalizedWord = normalizedIdiomWord,
                        PartOfSpeech = "phrase",
                        Definition = BuildIdiomDefinition(idiom),
                        RawFragment = idiomRawFragment,
                        SenseNumber = 1,
                        SourceCode = SourceCode,
                        CreatedUtc = DateTime.UtcNow
                    });
                }

                Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));

                logger.LogDebug(
                    "Century21Transformer processed entry | Word={Word} | EntriesCreated={Count} | RawFragmentLength={Length} | HasHtml={HasHtml}",
                    raw.Headword,
                    entries.Count,
                    entries.FirstOrDefault()?.RawFragment?.Length ?? 0,
                    entries.FirstOrDefault()?.RawFragment?.Contains("<div") ?? false);
            }
            catch (Exception ex)
            {
                Helper.HandleError(logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }

        // ✅ HtmlAgilityPack HtmlEntity does not provide Encode()
        // ✅ Use WebUtility.HtmlEncode for safe encoding in .NET 8
        private static string HtmlEncode(string? value)
            => WebUtility.HtmlEncode(value ?? string.Empty);

        // ✅ Build HTML structure that Century21DefinitionParser expects
        private static string BuildHtmlRawFragment(Century21RawEntry raw)
        {
            // Create proper HTML structure
            var html = new StringBuilder();
            html.AppendLine("<div class=\"word_block\">");

            if (!string.IsNullOrWhiteSpace(raw.Headword))
            {
                html.AppendLine($"  <span class=\"headword\">{HtmlEncode(raw.Headword)}</span>");
            }

            if (!string.IsNullOrWhiteSpace(raw.Phonetics))
            {
                html.AppendLine($"  <div class=\"sound_notation\">");
                html.AppendLine($"    <span class=\"phonetics\">{HtmlEncode(raw.Phonetics)}</span>");
                html.AppendLine($"  </div>");
            }

            html.AppendLine($"  <div class=\"basic_def\">");

            if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
            {
                html.AppendLine($"    <span class=\"pos\">{HtmlEncode(raw.PartOfSpeech)}</span>");
            }

            if (!string.IsNullOrWhiteSpace(raw.Definition))
            {
                html.AppendLine($"    <span class=\"definition\">{HtmlEncode(raw.Definition)}</span>");
            }

            if (!string.IsNullOrWhiteSpace(raw.GrammarInfo))
            {
                html.AppendLine($"    <span class=\"gram\">{HtmlEncode(raw.GrammarInfo)}</span>");
            }

            html.AppendLine($"  </div>");

            // Add examples
            foreach (var example in raw.Examples)
            {
                html.AppendLine($"  <div class=\"example\">");
                if (!string.IsNullOrWhiteSpace(example.English))
                {
                    html.AppendLine($"    <span class=\"ex_en\">{HtmlEncode(example.English)}</span>");
                }
                if (!string.IsNullOrWhiteSpace(example.Chinese))
                {
                    html.AppendLine($"    <span class=\"ex_cn\">{HtmlEncode(example.Chinese)}</span>");
                }
                html.AppendLine($"  </div>");
            }

            html.AppendLine("</div>");

            return html.ToString();
        }

        private static string BuildVariantHtmlFragment(string headword, Country21Variant variant)
        {
            var htmlDoc = new HtmlDocument();

            var variantDiv = htmlDoc.CreateElement("div");
            variantDiv.AddClass("variant");

            var itemDiv = htmlDoc.CreateElement("div");
            itemDiv.AddClass("item");

            if (!string.IsNullOrWhiteSpace(variant.PartOfSpeech))
            {
                var posSpan = htmlDoc.CreateElement("span");
                posSpan.AddClass("pos");
                posSpan.InnerHtml = HtmlEncode(variant.PartOfSpeech);
                itemDiv.AppendChild(posSpan);
            }

            if (!string.IsNullOrWhiteSpace(variant.Definition))
            {
                var definitionSpan = htmlDoc.CreateElement("span");
                definitionSpan.AddClass("definition");
                definitionSpan.InnerHtml = HtmlEncode(variant.Definition);
                itemDiv.AppendChild(definitionSpan);
            }

            if (variant.Examples.Any())
            {
                foreach (var example in variant.Examples)
                {
                    var exampleDiv = htmlDoc.CreateElement("div");
                    exampleDiv.AddClass("example");

                    if (!string.IsNullOrWhiteSpace(example.English))
                    {
                        var englishSpan = htmlDoc.CreateElement("span");
                        englishSpan.AddClass("ex_en");
                        englishSpan.InnerHtml = HtmlEncode(example.English);
                        exampleDiv.AppendChild(englishSpan);
                    }

                    if (!string.IsNullOrWhiteSpace(example.Chinese))
                    {
                        var chineseSpan = htmlDoc.CreateElement("span");
                        chineseSpan.AddClass("ex_cn");
                        chineseSpan.InnerHtml = HtmlEncode(example.Chinese);
                        exampleDiv.AppendChild(chineseSpan);
                    }

                    itemDiv.AppendChild(exampleDiv);
                }
            }

            variantDiv.AppendChild(itemDiv);
            htmlDoc.DocumentNode.AppendChild(variantDiv);
            return htmlDoc.DocumentNode.OuterHtml;
        }

        private static string BuildIdiomHtmlFragment(Country21Idiom idiom)
        {
            var htmlDoc = new HtmlDocument();

            var idiomDiv = htmlDoc.CreateElement("div");
            idiomDiv.AddClass("idiom");

            var itemDiv = htmlDoc.CreateElement("div");
            itemDiv.AddClass("item");

            if (!string.IsNullOrWhiteSpace(idiom.Headword))
            {
                var headwordSpan = htmlDoc.CreateElement("span");
                headwordSpan.AddClass("headword");
                headwordSpan.InnerHtml = HtmlEncode(idiom.Headword);
                itemDiv.AppendChild(headwordSpan);
            }

            if (!string.IsNullOrWhiteSpace(idiom.Definition))
            {
                var definitionSpan = htmlDoc.CreateElement("span");
                definitionSpan.AddClass("definition");
                definitionSpan.InnerHtml = HtmlEncode(idiom.Definition);
                itemDiv.AppendChild(definitionSpan);
            }

            if (idiom.Examples.Any())
            {
                foreach (var example in idiom.Examples)
                {
                    var exampleDiv = htmlDoc.CreateElement("div");
                    exampleDiv.AddClass("example");

                    if (!string.IsNullOrWhiteSpace(example.English))
                    {
                        var englishSpan = htmlDoc.CreateElement("span");
                        englishSpan.AddClass("ex_en");
                        englishSpan.InnerHtml = HtmlEncode(example.English);
                        exampleDiv.AppendChild(englishSpan);
                    }

                    if (!string.IsNullOrWhiteSpace(example.Chinese))
                    {
                        var chineseSpan = htmlDoc.CreateElement("span");
                        chineseSpan.AddClass("ex_cn");
                        chineseSpan.InnerHtml = HtmlEncode(example.Chinese);
                        exampleDiv.AppendChild(chineseSpan);
                    }

                    itemDiv.AppendChild(exampleDiv);
                }
            }

            idiomDiv.AppendChild(itemDiv);
            htmlDoc.DocumentNode.AppendChild(idiomDiv);
            return htmlDoc.DocumentNode.OuterHtml;
        }

        private static string BuildDefinition(Century21RawEntry raw)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(raw.Phonetics))
                parts.Add($"【Pronunciation】{raw.Phonetics}");

            if (!string.IsNullOrWhiteSpace(raw.GrammarInfo))
                parts.Add($"【Grammar】{raw.GrammarInfo}");

            var definition = Helper.NormalizeDefinitionForSource(raw.Definition, SourceCode);
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