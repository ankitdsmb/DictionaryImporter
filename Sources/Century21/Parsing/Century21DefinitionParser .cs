using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Century21.Parsing;

public sealed class Century21DefinitionParser : IDictionaryDefinitionParser
{
    private readonly ILogger<Century21DefinitionParser> _logger;

    public Century21DefinitionParser(ILogger<Century21DefinitionParser> logger)
    {
        _logger = logger;
    }

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RawFragment))
        {
            // Fallback: if no raw HTML, use the basic definition
            return new List<ParsedDefinition>
            {
                new ParsedDefinition
                {
                    MeaningTitle = entry.Word ?? "unnamed sense",
                    Definition = entry.Definition ?? string.Empty,
                    RawFragment = entry.Definition ?? string.Empty,
                    SenseNumber = entry.SenseNumber,
                    Domain = null,
                    UsageLabel = null,
                    CrossReferences = new List<CrossReference>(),
                    Synonyms = null,
                    Alias = null
                }
            };
        }

        try
        {
            return ParseHtmlContent(entry.RawFragment, entry.Word, entry.SenseNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Century21 HTML for entry: {Word}", entry.Word);

            // Return fallback entry
            return new List<ParsedDefinition>
            {
                new ParsedDefinition
                {
                    MeaningTitle = entry.Word ?? "unnamed sense",
                    Definition = entry.Definition ?? string.Empty,
                    RawFragment = entry.RawFragment,
                    SenseNumber = entry.SenseNumber,
                    Domain = null,
                    UsageLabel = null,
                    CrossReferences = new List<CrossReference>(),
                    Synonyms = null,
                    Alias = null
                }
            };
        }
    }

    private IEnumerable<ParsedDefinition> ParseHtmlContent(string htmlContent, string? entryWord, int senseNumber)
    {
        var results = new List<ParsedDefinition>();

        // Parse the HTML content
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(htmlContent);

        // Get all word blocks (some entries may have multiple)
        var wordBlocks = htmlDoc.DocumentNode.SelectNodes("//div[@class='word_block']");

        if (wordBlocks == null || wordBlocks.Count == 0)
        {
            _logger.LogWarning("No word blocks found in Century21 HTML for: {Word}", entryWord);
            return results;
        }

        foreach (var wordBlock in wordBlocks)
        {
            try
            {
                var blockResults = ParseWordBlock(wordBlock, entryWord, senseNumber);
                results.AddRange(blockResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse word block for entry: {Word}", entryWord);
                // Continue with next word block
            }
        }

        return results;
    }

    private List<ParsedDefinition> ParseWordBlock(HtmlNode wordBlock, string? entryWord, int baseSenseNumber)
    {
        var results = new List<ParsedDefinition>();

        // Extract the English headword
        var englishHeadword = ExtractEnglishHeadword(wordBlock);
        if (string.IsNullOrWhiteSpace(englishHeadword))
        {
            englishHeadword = entryWord ?? "unnamed";
        }

        // Extract IPA pronunciation
        var ipaPronunciation = ExtractIpaPronunciation(wordBlock);

        // Extract part of speech
        var partOfSpeech = ExtractPartOfSpeech(wordBlock);

        // Extract English definitions (from English examples and patterns)
        var englishDefinitions = ExtractEnglishDefinitions(wordBlock);

        // Extract English examples (only English text, no Chinese)
        var englishExamples = ExtractEnglishExamples(wordBlock);

        // Extract grammar info (like plural forms)
        var grammarInfo = ExtractGrammarInfo(wordBlock);

        // Extract variants (different POS with same headword)
        var variants = ExtractVariants(wordBlock);

        // Extract idioms/phrases
        var idioms = ExtractIdioms(wordBlock);

        // Process main definitions
        var senseNumber = baseSenseNumber;
        if (englishDefinitions.Count > 0)
        {
            foreach (var definition in englishDefinitions)
            {
                var parsedDef = CreateParsedDefinition(
                    englishHeadword,
                    definition,
                    partOfSpeech,
                    ipaPronunciation,
                    grammarInfo,
                    englishExamples,
                    senseNumber++,
                    wordBlock.OuterHtml
                );
                results.Add(parsedDef);
            }
        }
        else
        {
            // If no English definitions found, create one with empty definition
            var parsedDef = CreateParsedDefinition(
                englishHeadword,
                string.Empty,
                partOfSpeech,
                ipaPronunciation,
                grammarInfo,
                englishExamples,
                senseNumber++,
                wordBlock.OuterHtml
            );
            results.Add(parsedDef);
        }

        // Process variants
        foreach (var variant in variants)
        {
            if (variant.EnglishDefinitions.Count > 0)
            {
                foreach (var definition in variant.EnglishDefinitions)
                {
                    var parsedDef = CreateParsedDefinition(
                        englishHeadword,
                        definition,
                        variant.PartOfSpeech,
                        ipaPronunciation, // Variants share pronunciation
                        variant.GrammarInfo,
                        variant.EnglishExamples,
                        senseNumber++,
                        wordBlock.OuterHtml
                    );
                    results.Add(parsedDef);
                }
            }
        }

        // Process idioms as separate entries
        foreach (var idiom in idioms)
        {
            if (!string.IsNullOrWhiteSpace(idiom.EnglishHeadword) &&
                !string.IsNullOrWhiteSpace(idiom.EnglishDefinition))
            {
                var parsedDef = new ParsedDefinition
                {
                    MeaningTitle = idiom.EnglishHeadword,
                    Definition = idiom.EnglishDefinition,
                    RawFragment = $"Idiom: {idiom.EnglishHeadword} - {idiom.EnglishDefinition}",
                    SenseNumber = 1,
                    Domain = null,
                    UsageLabel = "idiom",
                    CrossReferences = new List<CrossReference>(),
                    Synonyms = null,
                    Alias = null
                };
                results.Add(parsedDef);
            }
        }

        return results;
    }

    #region Field Extraction Methods - Each Field Separate

    /// <summary>
    /// Extracts the English headword from the word block
    /// </summary>
    private string ExtractEnglishHeadword(HtmlNode wordBlock)
    {
        try
        {
            var headwordNode = wordBlock.SelectSingleNode(".//span[@class='headword']");
            if (headwordNode == null)
                return string.Empty;

            var headword = headwordNode.InnerText.Trim();

            // Clean the headword - remove any trailing numbers (like "ABC 1")
            headword = Regex.Replace(headword, @"\s+\d+$", string.Empty);

            // Remove any non-English characters (keep letters, spaces, apostrophes, hyphens)
            headword = Regex.Replace(headword, @"[^A-Za-z\s\-\']", string.Empty);

            // Normalize whitespace
            headword = Regex.Replace(headword, @"\s+", " ").Trim();

            return headword;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract English headword");
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts IPA pronunciation from the word block
    /// </summary>
    private string? ExtractIpaPronunciation(HtmlNode wordBlock)
    {
        try
        {
            // Look for phonetics in sound_notation div
            var soundNotation = wordBlock.SelectSingleNode(".//div[@class='sound_notation']");
            if (soundNotation != null)
            {
                var phoneticsNode = soundNotation.SelectSingleNode(".//span[@class='phonetics']");
                if (phoneticsNode != null)
                {
                    var ipa = phoneticsNode.InnerText.Trim();

                    // Validate it's actually IPA (contains IPA characters or slashes)
                    if (ContainsIpaCharacters(ipa))
                    {
                        // Ensure proper formatting
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
            _logger.LogDebug(ex, "Failed to extract IPA pronunciation");
            return null;
        }
    }

    /// <summary>
    /// Extracts part of speech from the word block
    /// </summary>
    private string? ExtractPartOfSpeech(HtmlNode wordBlock)
    {
        try
        {
            // Look in basic_def first
            var basicDef = wordBlock.SelectSingleNode(".//div[@class='basic_def']");
            if (basicDef != null)
            {
                var posNode = basicDef.SelectSingleNode(".//span[@class='pos']");
                if (posNode != null)
                {
                    var pos = posNode.InnerText.Trim();
                    return NormalizePartOfSpeech(pos);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract part of speech");
            return null;
        }
    }

    /// <summary>
    /// Extracts English definitions by analyzing examples and patterns
    /// </summary>
    private List<string> ExtractEnglishDefinitions(HtmlNode wordBlock)
    {
        var definitions = new List<string>();

        try
        {
            // First, try to get definition from English examples
            var englishExamples = ExtractEnglishExamples(wordBlock);
            if (englishExamples.Count > 0)
            {
                // Try to infer definition from first example
                var firstExample = englishExamples.First();
                if (firstExample.Length > 20) // Only use substantial examples
                {
                    // Try to extract a definition-like pattern
                    var inferredDefinition = InferDefinitionFromExample(firstExample);
                    if (!string.IsNullOrWhiteSpace(inferredDefinition))
                    {
                        definitions.Add(inferredDefinition);
                    }
                }
            }

            // Also look for patterns in the definition spans that might contain English
            var definitionSpans = wordBlock.SelectNodes(".//span[@class='definition']");
            if (definitionSpans != null)
            {
                foreach (var span in definitionSpans)
                {
                    var text = span.InnerText.Trim();
                    if (IsPrimarilyEnglish(text) && text.Length > 3)
                    {
                        // Clean Chinese markers
                        text = RemoveChineseMarkers(text);
                        text = CleanEnglishText(text);

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            definitions.Add(text);
                        }
                    }
                }
            }

            // Remove duplicates
            definitions = definitions.Distinct().Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract English definitions");
        }

        return definitions;
    }

    /// <summary>
    /// Extracts English examples only (no Chinese)
    /// </summary>
    private List<string> ExtractEnglishExamples(HtmlNode wordBlock)
    {
        var examples = new List<string>();

        try
        {
            // Look for English examples in ex_en spans
            var englishExampleNodes = wordBlock.SelectNodes(".//span[@class='ex_en']");
            if (englishExampleNodes != null)
            {
                foreach (var node in englishExampleNodes)
                {
                    var example = node.InnerText.Trim();
                    if (!string.IsNullOrWhiteSpace(example) && IsPrimarilyEnglish(example))
                    {
                        example = CleanEnglishText(example);

                        // Ensure proper sentence structure
                        if (example.Length > 5 && !example.EndsWith(".") &&
                            !example.EndsWith("!") && !example.EndsWith("?"))
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
            _logger.LogDebug(ex, "Failed to extract English examples");
        }

        return examples.Distinct().ToList();
    }

    /// <summary>
    /// Extracts grammar information (like plural forms)
    /// </summary>
    private string? ExtractGrammarInfo(HtmlNode wordBlock)
    {
        try
        {
            var grammarNode = wordBlock.SelectSingleNode(".//span[@class='gram']");
            if (grammarNode != null)
            {
                var grammar = grammarNode.InnerText.Trim();
                if (IsPrimarilyEnglish(grammar))
                {
                    return CleanEnglishText(grammar);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract grammar info");
            return null;
        }
    }

    /// <summary>
    /// Extracts variant forms (different parts of speech or forms)
    /// </summary>
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

                            // Extract variant POS
                            var posNode = variantNode.SelectSingleNode(".//span[@class='pos']");
                            if (posNode != null)
                            {
                                variant.PartOfSpeech = NormalizePartOfSpeech(posNode.InnerText.Trim());
                            }

                            // Extract English definitions from variant
                            variant.EnglishDefinitions = ExtractEnglishDefinitions(variantNode);

                            // Extract English examples from variant
                            variant.EnglishExamples = ExtractEnglishExamples(variantNode);

                            // Extract grammar from variant
                            variant.GrammarInfo = ExtractGrammarInfo(variantNode);

                            if (variant.EnglishDefinitions.Count > 0 || variant.EnglishExamples.Count > 0)
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
            _logger.LogDebug(ex, "Failed to extract variants");
        }

        return variants;
    }

    /// <summary>
    /// Extracts idioms and phrases
    /// </summary>
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

                            // Extract idiom headword (English)
                            var headwordNode = idiomNode.SelectSingleNode(".//span[@class='headword']");
                            if (headwordNode != null)
                            {
                                idiom.EnglishHeadword = ExtractEnglishHeadword(idiomNode);
                            }

                            // Try to extract English definition from idiom
                            var definitionNode = idiomNode.SelectSingleNode(".//span[@class='definition']");
                            if (definitionNode != null)
                            {
                                var definition = definitionNode.InnerText.Trim();
                                if (IsPrimarilyEnglish(definition))
                                {
                                    idiom.EnglishDefinition = CleanEnglishText(definition);
                                }
                            }

                            // Extract English examples from idiom
                            idiom.EnglishExamples = ExtractEnglishExamples(idiomNode);

                            // If no English definition but has English examples, infer from examples
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
            _logger.LogDebug(ex, "Failed to extract idioms");
        }

        return idioms;
    }

    #endregion Field Extraction Methods - Each Field Separate

    #region Text Processing Helper Methods

    /// <summary>
    /// Cleans and normalizes English text
    /// </summary>
    private string CleanEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove HTML entities
        text = DecodeHtmlEntities(text);

        // Remove any Chinese characters
        text = RemoveChineseCharacters(text);

        // Remove Chinese brackets and markers
        text = RemoveChineseMarkers(text);

        // Remove any remaining HTML tags
        text = Regex.Replace(text, "<.*?>", string.Empty);

        // Remove extra whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    /// <summary>
    /// Removes Chinese characters from text
    /// </summary>
    private string RemoveChineseCharacters(string text)
    {
        // Remove CJK Unified Ideographs and other Chinese characters
        return Regex.Replace(text, @"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3000-\u303F\uff00-\uffef]", string.Empty);
    }

    /// <summary>
    /// Removes Chinese-specific punctuation and markers
    /// </summary>
    private string RemoveChineseMarkers(string text)
    {
        return text.Replace("〈", "")
                  .Replace("〉", "")
                  .Replace("《", "")
                  .Replace("》", "")
                  .Replace("。", ". ")
                  .Replace("，", ", ")
                  .Replace("；", "; ")
                  .Replace("：", ": ")
                  .Replace("？", "? ")
                  .Replace("！", "! ")
                  .Replace("（", "(")
                  .Replace("）", ")")
                  .Replace("【", "[")
                  .Replace("】", "]")
                  .Trim();
    }

    /// <summary>
    /// Decodes HTML entities
    /// </summary>
    private string DecodeHtmlEntities(string text)
    {
        return text.Replace("&nbsp;", " ")
                  .Replace("&lt;", "<")
                  .Replace("&gt;", ">")
                  .Replace("&amp;", "&")
                  .Replace("&quot;", "\"")
                  .Replace("&apos;", "'")
                  .Replace("&#39;", "'");
    }

    /// <summary>
    /// Normalizes part of speech tags
    /// </summary>
    private string NormalizePartOfSpeech(string pos)
    {
        if (string.IsNullOrWhiteSpace(pos))
            return "unk";

        var normalized = pos.Trim().ToLowerInvariant();

        // Remove trailing period if present
        if (normalized.EndsWith("."))
            normalized = normalized[..^1];

        // Map to standard POS tags used in the system
        return normalized switch
        {
            "n" => "noun",
            "v" => "verb",
            "vt" => "verb",
            "vi" => "verb",
            "adj" => "adj",
            "a" => "adj",
            "adv" => "adv",
            "ad" => "adv",
            "prep" => "preposition",
            "pron" => "pronoun",
            "conj" => "conjunction",
            "interj" => "exclamation",
            "exclam" => "exclamation",
            "abbr" => "abbreviation",
            "pref" => "prefix",
            "suf" => "suffix",
            "s" => "suffix",
            "num" => "numeral",
            "art" => "determiner",
            "det" => "determiner",
            "aux" => "auxiliary",
            "modal" => "modal",
            "phrase" => "phrase",
            "phr" => "phrase",
            "idm" => "idiom",
            "idiom" => "idiom",
            _ => normalized
        };
    }

    /// <summary>
    /// Checks if text contains primarily English characters
    /// </summary>
    private bool IsPrimarilyEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Count English vs non-English characters
        var englishChars = 0;
        var totalChars = 0;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                continue;

            totalChars++;

            // Check if character is in basic Latin range or common symbols
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') || c == '/' || c == '[' || c == ']' ||
                c == '(' || c == ')' || c == '-' || c == '\'')
            {
                englishChars++;
            }
        }

        // If no characters to check, return false
        if (totalChars == 0)
            return false;

        // Consider text primarily English if > 70% English characters
        return (englishChars * 100 / totalChars) > 70;
    }

    /// <summary>
    /// Checks if text contains IPA characters
    /// </summary>
    private bool ContainsIpaCharacters(string text)
    {
        // Check for common IPA symbols
        return Regex.IsMatch(text, @"[\/\[\]ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃]");
    }

    /// <summary>
    /// Infers definition from example sentence
    /// </summary>
    private string InferDefinitionFromExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        // Common patterns to extract definitions
        if (example.Contains(" means "))
        {
            var match = Regex.Match(example, @"(\w+)\s+means\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 2)
                return $"{match.Groups[1].Value} means {match.Groups[2].Value}";
        }

        if (example.Contains(" is "))
        {
            var match = Regex.Match(example, @"(\w+)\s+is\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 2)
                return $"{match.Groups[1].Value} is {match.Groups[2].Value}";
        }

        if (example.Contains(" refers to "))
        {
            var match = Regex.Match(example, @"(\w+)\s+refers to\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 2)
                return $"{match.Groups[1].Value} refers to {match.Groups[2].Value}";
        }

        // Generic fallback
        if (example.Length > 10 && example.Length < 100)
        {
            return example;
        }

        return string.Empty;
    }

    #endregion Text Processing Helper Methods

    #region Helper Methods

    /// <summary>
    /// Creates a ParsedDefinition object with all extracted information
    /// </summary>
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
            Definition = englishDefinition,
            RawFragment = rawFragment,
            SenseNumber = senseNumber,
            Domain = null,
            UsageLabel = !string.IsNullOrWhiteSpace(grammarInfo) ? grammarInfo : null,
            CrossReferences = new List<CrossReference>(),
            Synonyms = null,
            Alias = null
        };

        // Add examples if available
        if (englishExamples.Count > 0)
        {
            parsed.Examples = englishExamples;

            // Also append examples to definition for display
            if (!string.IsNullOrWhiteSpace(parsed.Definition))
            {
                parsed.Definition += "\n\nExamples:";
                foreach (var example in englishExamples.Take(3)) // Limit to 3 examples
                {
                    parsed.Definition += $"\n• {example}";
                }
            }
        }

        // Add IPA if available
        if (!string.IsNullOrWhiteSpace(ipaPronunciation))
        {
            parsed.Definition = $"Pronunciation: {ipaPronunciation}\n" + parsed.Definition;
        }

        // Add part of speech if available
        if (!string.IsNullOrWhiteSpace(partOfSpeech) && partOfSpeech != "unk")
        {
            parsed.Definition = $"({partOfSpeech}) " + parsed.Definition;
        }

        return parsed;
    }

    #endregion Helper Methods

    #region Helper Classes

    private class VariantInfo
    {
        public string? PartOfSpeech { get; set; }
        public List<string> EnglishDefinitions { get; set; } = new();
        public List<string> EnglishExamples { get; set; } = new();
        public string? GrammarInfo { get; set; }
    }

    private class IdiomInfo
    {
        public string EnglishHeadword { get; set; } = null!;
        public string EnglishDefinition { get; set; } = null!;
        public List<string> EnglishExamples { get; set; } = new();
    }

    #endregion Helper Classes
}