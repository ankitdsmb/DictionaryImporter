using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Century21.Parsing
{
    public sealed class Century21DefinitionParser(
        ILogger<Century21DefinitionParser> logger)
        : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "CENTURY21";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            // ✅ Never return empty list
            if (string.IsNullOrWhiteSpace(entry.RawFragment))
                return new List<ParsedDefinition> { CreateFallback(entry) };

            try
            {
                var parsed = ParseHtmlContent(
                    entry.RawFragment,
                    entry.Word,
                    entry.SenseNumber);

                // ✅ If nothing parsed, return fallback definition
                var list = parsed.ToList();
                if (list.Count == 0)
                    return new List<ParsedDefinition> { CreateFallback(entry) };

                return list;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to parse Century21 HTML for entry: {Word}",
                    entry.Word);

                return new List<ParsedDefinition> { CreateFallback(entry) };
            }
        }

        private ParsedDefinition CreateFallback(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = entry.Definition ?? string.Empty,
                RawFragment = entry.RawFragment ?? entry.Definition ?? string.Empty,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        private IEnumerable<ParsedDefinition> ParseHtmlContent(
            string htmlContent,
            string? entryWord,
            int senseNumber)
        {
            var results = new List<ParsedDefinition>();

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            var wordBlocks =
                htmlDoc.DocumentNode.SelectNodes("//div[@class='word_block']");

            if (wordBlocks == null || wordBlocks.Count == 0)
            {
                logger.LogWarning(
                    "No word blocks found in Century21 HTML for: {Word}",
                    entryWord);

                return results;
            }

            foreach (var wordBlock in wordBlocks)
            {
                try
                {
                    var blockResults = ParseWordBlock(wordBlock, entryWord, senseNumber);

                    if (blockResults.Count > 0)
                        results.AddRange(blockResults);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Failed to parse word block for entry: {Word}",
                        entryWord);
                }
            }

            return results;
        }

        private List<ParsedDefinition> ParseWordBlock(
            HtmlNode wordBlock,
            string? entryWord,
            int baseSenseNumber)
        {
            var results = new List<ParsedDefinition>();

            var englishHeadword = ExtractEnglishHeadword(wordBlock);
            if (string.IsNullOrWhiteSpace(englishHeadword))
                englishHeadword = entryWord ?? "unnamed";

            var ipaPronunciation = ExtractIpaPronunciation(wordBlock);
            var partOfSpeech = ExtractPartOfSpeech(wordBlock);

            var englishDefinitions = ExtractEnglishDefinitions(wordBlock);
            var englishExamples = ExtractEnglishExamples(wordBlock);
            var grammarInfo = ExtractGrammarInfo(wordBlock);

            var variants = ExtractVariants(wordBlock);
            var idioms = ExtractIdioms(wordBlock);

            var senseNumber = baseSenseNumber;

            // Main definitions
            if (englishDefinitions.Count > 0)
            {
                foreach (var definition in englishDefinitions)
                {
                    results.Add(CreateParsedDefinition(
                        englishHeadword,
                        definition,
                        partOfSpeech,
                        ipaPronunciation,
                        grammarInfo,
                        englishExamples,
                        senseNumber++,
                        wordBlock.OuterHtml));
                }
            }
            else
            {
                // Still return something
                results.Add(CreateParsedDefinition(
                    englishHeadword,
                    string.Empty,
                    partOfSpeech,
                    ipaPronunciation,
                    grammarInfo,
                    englishExamples,
                    senseNumber++,
                    wordBlock.OuterHtml));
            }

            // Variants
            foreach (var variant in variants)
            {
                if (variant.EnglishDefinitions.Count <= 0)
                    continue;

                foreach (var definition in variant.EnglishDefinitions)
                {
                    results.Add(CreateParsedDefinition(
                        englishHeadword,
                        definition,
                        variant.PartOfSpeech,
                        ipaPronunciation,
                        variant.GrammarInfo,
                        variant.EnglishExamples,
                        senseNumber++,
                        wordBlock.OuterHtml));
                }
            }

            // Idioms
            foreach (var idiom in idioms)
            {
                if (string.IsNullOrWhiteSpace(idiom.EnglishHeadword) ||
                    string.IsNullOrWhiteSpace(idiom.EnglishDefinition))
                    continue;

                var parsedDef = new ParsedDefinition
                {
                    MeaningTitle = idiom.EnglishHeadword,
                    Definition = SourceDataHelper.NormalizeDefinition(idiom.EnglishDefinition),
                    RawFragment = $"Idiom: {idiom.EnglishHeadword} - {idiom.EnglishDefinition}",
                    SenseNumber = 1,
                    Domain = null,
                    UsageLabel = "idiom",
                    CrossReferences = new List<CrossReference>(),
                    Synonyms = null,
                    Alias = null
                };

                if (idiom.EnglishExamples.Count > 0)
                    parsedDef.Examples = idiom.EnglishExamples;

                results.Add(parsedDef);
            }

            return results;
        }

        #region Field Extraction Methods

        private string ExtractEnglishHeadword(HtmlNode wordBlock)
        {
            try
            {
                var headwordNode =
                    wordBlock.SelectSingleNode(".//span[@class='headword']");

                if (headwordNode == null)
                    return string.Empty;

                var headword = headwordNode.InnerText.Trim();

                headword = Regex.Replace(headword, @"\s+\d+$", string.Empty);
                headword = Regex.Replace(headword, @"[^A-Za-z\s\-\']", string.Empty);
                headword = Regex.Replace(headword, @"\s+", " ").Trim();

                return headword;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract English headword");
                return string.Empty;
            }
        }

        private string? ExtractIpaPronunciation(HtmlNode wordBlock)
        {
            try
            {
                var soundNotation =
                    wordBlock.SelectSingleNode(".//div[@class='sound_notation']");

                if (soundNotation != null)
                {
                    var phoneticsNode =
                        soundNotation.SelectSingleNode(".//span[@class='phonetics']");

                    if (phoneticsNode != null)
                    {
                        var ipa = phoneticsNode.InnerText.Trim();

                        if (Century21TextHelper.ContainsIpaCharacters(ipa))
                        {
                            if (!ipa.StartsWith("/") && !ipa.StartsWith("["))
                                ipa = "/" + ipa + "/";

                            return ipa;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract IPA pronunciation");
                return null;
            }
        }

        private string? ExtractPartOfSpeech(HtmlNode wordBlock)
        {
            try
            {
                var basicDef = wordBlock.SelectSingleNode(".//div[@class='basic_def']");
                if (basicDef != null)
                {
                    var posNode = basicDef.SelectSingleNode(".//span[@class='pos']");
                    if (posNode != null)
                    {
                        var pos = posNode.InnerText.Trim();
                        return SourceDataHelper.NormalizePartOfSpeech(pos);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract part of speech");
                return null;
            }
        }

        private List<string> ExtractEnglishDefinitions(HtmlNode wordBlock)
        {
            var definitions = new List<string>();

            try
            {
                var englishExamples = ExtractEnglishExamples(wordBlock);

                // Infer definition from first example (your existing logic)
                if (englishExamples.Count > 0)
                {
                    var firstExample = englishExamples.First();
                    if (firstExample.Length > 20)
                    {
                        var inferredDefinition =
                            Century21TextHelper.InferDefinitionFromExample(firstExample);

                        if (!string.IsNullOrWhiteSpace(inferredDefinition))
                            definitions.Add(inferredDefinition);
                    }
                }

                var definitionSpans = wordBlock.SelectNodes(".//span[@class='definition']");
                if (definitionSpans != null)
                {
                    foreach (var span in definitionSpans)
                    {
                        var text = span.InnerText.Trim();

                        if (Century21TextHelper.IsPrimarilyEnglish(text) && text.Length > 3)
                        {
                            text = Century21TextHelper.RemoveChineseMarkers(text);
                            text = Century21TextHelper.CleanEnglishText(text);

                            if (!string.IsNullOrWhiteSpace(text))
                                definitions.Add(text);
                        }
                    }
                }

                return definitions
                    .Distinct()
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract English definitions");
                return definitions;
            }
        }

        private List<string> ExtractEnglishExamples(HtmlNode wordBlock)
        {
            var examples = new List<string>();

            try
            {
                var englishExampleNodes = wordBlock.SelectNodes(".//span[@class='ex_en']");
                if (englishExampleNodes != null)
                {
                    foreach (var node in englishExampleNodes)
                    {
                        var example = node.InnerText.Trim();

                        if (!string.IsNullOrWhiteSpace(example) &&
                            Century21TextHelper.IsPrimarilyEnglish(example))
                        {
                            example = Century21TextHelper.CleanEnglishText(example);

                            if (example.Length > 5 &&
                                !example.EndsWith(".") &&
                                !example.EndsWith("!") &&
                                !example.EndsWith("?"))
                            {
                                example += ".";
                            }

                            examples.Add(example);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract English examples");
            }

            return examples.Distinct().ToList();
        }

        private string? ExtractGrammarInfo(HtmlNode wordBlock)
        {
            try
            {
                var grammarNode = wordBlock.SelectSingleNode(".//span[@class='gram']");
                if (grammarNode != null)
                {
                    var grammar = grammarNode.InnerText.Trim();
                    if (Century21TextHelper.IsPrimarilyEnglish(grammar))
                        return Century21TextHelper.CleanEnglishText(grammar);
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract grammar info");
                return null;
            }
        }

        private List<VariantInfo> ExtractVariants(HtmlNode wordBlock)
        {
            var variants = new List<VariantInfo>();

            try
            {
                var variantSections = wordBlock.SelectNodes(".//div[@class='variant']");
                if (variantSections != null)
                {
                    foreach (var variantSection in variantSections)
                    {
                        var variantNodes = variantSection.SelectNodes(".//div[@class='item']");
                        if (variantNodes != null)
                        {
                            foreach (var variantNode in variantNodes)
                            {
                                var variant = new VariantInfo();

                                var posNode = variantNode.SelectSingleNode(".//span[@class='pos']");
                                if (posNode != null)
                                    variant.PartOfSpeech = SourceDataHelper.NormalizePartOfSpeech(posNode.InnerText.Trim());

                                variant.EnglishDefinitions = ExtractEnglishDefinitions(variantNode);
                                variant.EnglishExamples = ExtractEnglishExamples(variantNode);
                                variant.GrammarInfo = ExtractGrammarInfo(variantNode);

                                if (variant.EnglishDefinitions.Count > 0 ||
                                    variant.EnglishExamples.Count > 0)
                                {
                                    variants.Add(variant);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract variants");
            }

            return variants;
        }

        private List<IdiomInfo> ExtractIdioms(HtmlNode wordBlock)
        {
            var idioms = new List<IdiomInfo>();

            try
            {
                var idiomSections = wordBlock.SelectNodes(".//div[@class='idiom']");
                if (idiomSections != null)
                {
                    foreach (var idiomSection in idiomSections)
                    {
                        var idiomNodes = idiomSection.SelectNodes(".//div[@class='item']");
                        if (idiomNodes != null)
                        {
                            foreach (var idiomNode in idiomNodes)
                            {
                                var idiom = new IdiomInfo();

                                var headwordNode = idiomNode.SelectSingleNode(".//span[@class='headword']");
                                if (headwordNode != null)
                                    idiom.EnglishHeadword = ExtractEnglishHeadword(idiomNode);

                                var definitionNode = idiomNode.SelectSingleNode(".//span[@class='definition']");
                                if (definitionNode != null)
                                {
                                    var definition = definitionNode.InnerText.Trim();
                                    if (Century21TextHelper.IsPrimarilyEnglish(definition))
                                        idiom.EnglishDefinition = Century21TextHelper.CleanEnglishText(definition);
                                }

                                idiom.EnglishExamples = ExtractEnglishExamples(idiomNode);

                                if (string.IsNullOrWhiteSpace(idiom.EnglishDefinition) &&
                                    idiom.EnglishExamples.Count > 0)
                                {
                                    idiom.EnglishDefinition = $"Idiomatic expression: {idiom.EnglishHeadword}";
                                }

                                if (!string.IsNullOrWhiteSpace(idiom.EnglishHeadword) &&
                                    !string.IsNullOrWhiteSpace(idiom.EnglishDefinition))
                                {
                                    idioms.Add(idiom);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract idioms");
            }

            return idioms;
        }

        #endregion Field Extraction Methods

        #region Helper Methods

        private ParsedDefinition CreateParsedDefinition(
            string englishHeadword,
            string englishDefinition,
            string? partOfSpeech,
            string? ipaPronunciation,
            string? grammarInfo,
            List<string> englishExamples,
            int senseNumber,
            string rawFragment)
        {
            var parsed = new ParsedDefinition
            {
                MeaningTitle = englishHeadword,
                Definition = SourceDataHelper.NormalizeDefinition(englishDefinition ?? string.Empty),
                RawFragment = rawFragment,
                SenseNumber = senseNumber,
                Domain = null,
                UsageLabel = !string.IsNullOrWhiteSpace(grammarInfo) ? grammarInfo : null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };

            if (englishExamples.Count > 0)
                parsed.Examples = englishExamples;

            if (!string.IsNullOrWhiteSpace(ipaPronunciation))
                parsed.Definition = $"Pronunciation: {ipaPronunciation}\n" + parsed.Definition;

            if (!string.IsNullOrWhiteSpace(partOfSpeech) && partOfSpeech != "unk")
                parsed.Definition = $"({partOfSpeech}) " + parsed.Definition;

            return parsed;
        }

        #endregion Helper Methods

        #region Helper Classes

        private sealed class VariantInfo
        {
            public string? PartOfSpeech { get; set; }
            public List<string> EnglishDefinitions { get; set; } = [];
            public List<string> EnglishExamples { get; set; } = [];
            public string? GrammarInfo { get; set; }
        }

        private sealed class IdiomInfo
        {
            public string EnglishHeadword { get; set; } = string.Empty;
            public string EnglishDefinition { get; set; } = string.Empty;
            public List<string> EnglishExamples { get; set; } = [];
        }

        #endregion Helper Classes
    }
}