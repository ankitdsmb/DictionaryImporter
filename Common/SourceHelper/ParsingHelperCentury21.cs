using HtmlAgilityPack;

namespace DictionaryImporter.Common.SourceHelper;

public static class ParsingHelperCentury21
{
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ChineseCharRegex = new(@"[\u4E00-\u9FFF\u3400-\u4DBF]", RegexOptions.Compiled);
    private static readonly Regex FullChineseCharRegex = new(@"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3000-\u303F\uff00-\uffef]", RegexOptions.Compiled);
    private static readonly Regex IpaCharRegex = new(@"[\/\[\]ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃]", RegexOptions.Compiled);
    private static readonly Regex MeansRegex = new(@"(\w+)\s+means\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IsRegex = new(@"(\w+)\s+is\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RefersToRegex = new(@"(\w+)\s+refers to\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string CleanEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = DecodeHtmlEntities(text);
        text = HtmlTagRegex.Replace(text, string.Empty);
        text = WhitespaceRegex.Replace(text, " ").Trim();

        return text;
    }

    public static string DecodeHtmlEntities(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return HtmlEntity.DeEntitize(text);
    }

    public static bool IsPrimarilyEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        int englishChars = 0;
        int totalChars = 0;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) continue;

            totalChars++;

            if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' ||
                c == '/' || c == '[' || c == ']' || c == '(' || c == ')' || c == '-' || c == '\'')
            {
                englishChars++;
            }
        }

        if (totalChars == 0) return false;
        return englishChars * 100 / totalChars > 70;
    }

    public static bool ContainsIpaCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return IpaCharRegex.IsMatch(text);
    }

    public static string InferDefinitionFromExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example)) return string.Empty;

        // Check for common definition patterns
        var meansMatch = MeansRegex.Match(example);
        if (meansMatch.Success && meansMatch.Groups.Count > 2)
            return $"{meansMatch.Groups[1].Value} means {meansMatch.Groups[2].Value}";

        var isMatch = IsRegex.Match(example);
        if (isMatch.Success && isMatch.Groups.Count > 2)
            return $"{isMatch.Groups[1].Value} is {isMatch.Groups[2].Value}";

        var refersToMatch = RefersToRegex.Match(example);
        if (refersToMatch.Success && refersToMatch.Groups.Count > 2)
            return $"{refersToMatch.Groups[1].Value} refers to {refersToMatch.Groups[2].Value}";

        // Fallback for short examples
        if (example.Length > 10 && example.Length < 100)
            return example;

        return string.Empty;
    }

    public static Century21ParsedData ParseCentury21Html(string htmlContent, string? entryWord = null)
    {
        var data = new Century21ParsedData();

        if (string.IsNullOrWhiteSpace(htmlContent))
            return data;

        try
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            // Find word blocks
            var wordBlocks = htmlDoc.DocumentNode.SelectNodes("//div[@class='word_block']");
            if (wordBlocks == null || wordBlocks.Count == 0)
                return data;

            // Parse first word block
            var wordBlock = wordBlocks[0];
            ParseWordBlock(wordBlock, data, entryWord);
        }
        catch (Exception ex)
        {
            // Log error if logger available
            Debug.WriteLine($"Century21 parsing error: {ex.Message}");
        }

        return data;
    }

    private static void ParseWordBlock(HtmlNode wordBlock, Century21ParsedData data, string? entryWord)
    {
        // Extract headword
        data.Headword = ExtractHeadword(wordBlock) ?? entryWord ?? "unnamed";

        // Extract IPA pronunciation
        data.IpaPronunciation = ExtractIpaPronunciation(wordBlock);

        // Extract part of speech
        data.PartOfSpeech = ExtractPartOfSpeech(wordBlock);

        // Extract grammar info
        data.GrammarInfo = ExtractGrammarInfo(wordBlock);

        // Extract main definitions
        data.Definitions = ExtractDefinitions(wordBlock);

        // Extract examples
        data.Examples = ExtractExamples(wordBlock);

        // Extract variants
        data.Variants = ExtractVariants(wordBlock);

        // Extract idioms
        data.Idioms = ExtractIdioms(wordBlock);
    }

    public static string? ExtractHeadword(HtmlNode wordBlock)
    {
        try
        {
            var headwordNode = wordBlock.SelectSingleNode(".//span[@class='headword']");
            if (headwordNode == null)
                return null;

            return CleanText(headwordNode.InnerText);
        }
        catch
        {
            return null;
        }
    }

    public static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Decode HTML entities safely
        text = HtmlEntity.DeEntitize(text);

        return text.Trim();
    }

    public static string? ExtractIpaPronunciation(HtmlNode wordBlock)
    {
        try
        {
            var soundNotation = wordBlock.SelectSingleNode(".//div[@class='sound_notation']");
            if (soundNotation != null)
            {
                var phoneticsNode = soundNotation.SelectSingleNode(".//span[@class='phonetics']");
                if (phoneticsNode != null)
                {
                    var ipa = phoneticsNode.InnerText.Trim();
                    if (ContainsIpaCharacters(ipa))
                    {
                        // Ensure IPA is properly formatted
                        if (!ipa.StartsWith("/") && !ipa.StartsWith("["))
                            ipa = "/" + ipa + "/";
                        return ipa;
                    }
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtractPartOfSpeech(HtmlNode wordBlock)
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
                    return Helper.NormalizePartOfSpeech(pos);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtractGrammarInfo(HtmlNode wordBlock)
    {
        try
        {
            var grammarNode = wordBlock.SelectSingleNode(".//span[@class='gram']");
            if (grammarNode != null)
            {
                var grammar = grammarNode.InnerText.Trim();
                if (IsPrimarilyEnglish(grammar))
                    return CleanEnglishText(grammar);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> ExtractDefinitions(HtmlNode wordBlock)
    {
        var definitions = new List<string>();

        try
        {
            // First try to extract from definition spans
            var definitionSpans = wordBlock.SelectNodes(".//span[@class='definition']");
            if (definitionSpans != null)
            {
                foreach (var span in definitionSpans)
                {
                    var text = span.InnerText.Trim();
                    if (text.Length > 3)
                    {
                        // Clean formatting but preserve bilingual content
                        text = CleanEnglishText(text);
                        if (!string.IsNullOrWhiteSpace(text))
                            definitions.Add(text);
                    }
                }
            }

            // If no definitions found, try to infer from examples
            if (definitions.Count == 0)
            {
                var examples = ExtractExamples(wordBlock);
                if (examples.Count > 0)
                {
                    var firstExample = examples.First();
                    if (firstExample.Length > 20)
                    {
                        var inferredDefinition = InferDefinitionFromExample(firstExample);
                        if (!string.IsNullOrWhiteSpace(inferredDefinition))
                            definitions.Add(inferredDefinition);
                    }
                }
            }

            return definitions.Distinct().Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        }
        catch
        {
            return definitions;
        }
    }

    public static IReadOnlyList<string> ExtractExamples(HtmlNode wordBlock)
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
                    if (!string.IsNullOrWhiteSpace(example) && IsPrimarilyEnglish(example))
                    {
                        example = CleanEnglishText(example);

                        // Ensure proper punctuation
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
            return examples.Distinct().ToList();
        }
        catch
        {
            return examples;
        }
    }

    public static IReadOnlyList<Century21VariantData> ExtractVariants(HtmlNode wordBlock)
    {
        var variants = new List<Century21VariantData>();

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
                            var variant = new Century21VariantData();

                            // Extract POS
                            var posNode = variantNode.SelectSingleNode(".//span[@class='pos']");
                            if (posNode != null)
                                variant.PartOfSpeech = Helper.NormalizePartOfSpeech(posNode.InnerText.Trim());

                            // Extract definitions
                            variant.Definitions = ExtractDefinitionsFromNode(variantNode);

                            // Extract examples
                            variant.Examples = ExtractExamplesFromNode(variantNode);

                            // Extract grammar info
                            variant.GrammarInfo = ExtractGrammarInfoFromNode(variantNode);

                            if (variant.Definitions.Count > 0 || variant.Examples.Count > 0)
                            {
                                variants.Add(variant);
                            }
                        }
                    }
                }
            }
            return variants;
        }
        catch
        {
            return variants;
        }
    }

    public static IReadOnlyList<Century21IdiomData> ExtractIdioms(HtmlNode wordBlock)
    {
        var idioms = new List<Century21IdiomData>();

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
                            var idiom = new Century21IdiomData();

                            // Extract headword
                            var headwordNode = idiomNode.SelectSingleNode(".//span[@class='headword']");
                            if (headwordNode != null)
                                idiom.Headword = ExtractHeadword(idiomNode);

                            // Extract definition
                            var definitionNode = idiomNode.SelectSingleNode(".//span[@class='definition']");
                            if (definitionNode != null)
                            {
                                var definition = definitionNode.InnerText.Trim();
                                if (IsPrimarilyEnglish(definition))
                                    idiom.Definition = CleanEnglishText(definition);
                            }

                            // Extract examples
                            idiom.Examples = ExtractExamplesFromNode(idiomNode);

                            // If no definition but has examples, create a default definition
                            if (string.IsNullOrWhiteSpace(idiom.Definition) && idiom.Examples.Count > 0)
                            {
                                idiom.Definition = $"Idiomatic expression: {idiom.Headword}";
                            }

                            if (!string.IsNullOrWhiteSpace(idiom.Headword) && !string.IsNullOrWhiteSpace(idiom.Definition))
                            {
                                idioms.Add(idiom);
                            }
                        }
                    }
                }
            }
            return idioms;
        }
        catch
        {
            return idioms;
        }
    }

    public static string? ExtractDomain(HtmlNode wordBlock)
    {
        // Century21 doesn't have explicit domain labels like Oxford
        // Domain can be inferred from grammar info or definition content

        try
        {
            // Check grammar info for domain hints
            var grammarInfo = ExtractGrammarInfo(wordBlock);
            if (!string.IsNullOrWhiteSpace(grammarInfo))
            {
                // Check if grammar info contains domain-like information
                if (grammarInfo.Contains("(BrE)", StringComparison.OrdinalIgnoreCase) ||
                    grammarInfo.Contains("British", StringComparison.OrdinalIgnoreCase))
                    return "UK";

                if (grammarInfo.Contains("(AmE)", StringComparison.OrdinalIgnoreCase) ||
                    grammarInfo.Contains("American", StringComparison.OrdinalIgnoreCase))
                    return "US";

                if (grammarInfo.Contains("formal", StringComparison.OrdinalIgnoreCase))
                    return "formal";

                if (grammarInfo.Contains("informal", StringComparison.OrdinalIgnoreCase))
                    return "informal";
            }

            // Check definitions for domain hints
            var definitions = ExtractDefinitions(wordBlock);
            foreach (var definition in definitions)
            {
                var lowerDef = definition.ToLowerInvariant();

                if (lowerDef.Contains("【domain】") || lowerDef.Contains("【语域】"))
                {
                    // Extract domain from marker
                    var domainMatch = Regex.Match(definition, @"【(?:domain|语域)】：\s*([^】]+)】", RegexOptions.IgnoreCase);
                    if (domainMatch.Success)
                        return domainMatch.Groups[1].Value.Trim();
                }

                // Check for common domain indicators
                if (lowerDef.Contains("music") || lowerDef.Contains("musical") ||
                    lowerDef.Contains("〈音〉") || lowerDef.Contains("音"))
                    return "music";

                if (lowerDef.Contains("grammar") || lowerDef.Contains("grammatical"))
                    return "grammar";

                if (lowerDef.Contains("biology") || lowerDef.Contains("biological"))
                    return "biology";

                if (lowerDef.Contains("chemistry") || lowerDef.Contains("chemical"))
                    return "chemistry";

                if (lowerDef.Contains("physics") || lowerDef.Contains("physical"))
                    return "physics";

                if (lowerDef.Contains("mathematics") || lowerDef.Contains("math"))
                    return "mathematics";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtractUsageLabel(HtmlNode wordBlock)
    {
        // Century21 usage label is typically the grammar info
        return ExtractGrammarInfo(wordBlock);
    }

    private static IReadOnlyList<string> ExtractDefinitionsFromNode(HtmlNode node)
    {
        var definitions = new List<string>();

        try
        {
            var definitionNodes = node.SelectNodes(".//span[@class='definition']");
            if (definitionNodes != null)
            {
                foreach (var defNode in definitionNodes)
                {
                    var text = defNode.InnerText.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        text = CleanEnglishText(text);
                        definitions.Add(text);
                    }
                }
            }
            return definitions;
        }
        catch
        {
            return definitions;
        }
    }

    private static IReadOnlyList<string> ExtractExamplesFromNode(HtmlNode node)
    {
        var examples = new List<string>();

        try
        {
            var exampleNodes = node.SelectNodes(".//span[@class='ex_en']");
            if (exampleNodes != null)
            {
                foreach (var exNode in exampleNodes)
                {
                    var example = exNode.InnerText.Trim();
                    if (!string.IsNullOrWhiteSpace(example))
                    {
                        example = CleanEnglishText(example);
                        examples.Add(example);
                    }
                }
            }
            return examples;
        }
        catch
        {
            return examples;
        }
    }

    private static string? ExtractGrammarInfoFromNode(HtmlNode node)
    {
        try
        {
            var grammarNode = node.SelectSingleNode(".//span[@class='gram']");
            if (grammarNode != null)
            {
                var grammar = grammarNode.InnerText.Trim();
                return CleanEnglishText(grammar);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}