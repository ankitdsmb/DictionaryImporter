using DictionaryImporter.Sources.Century21.Models;
using HtmlAgilityPack;

namespace DictionaryImporter.Sources.Century21;

public sealed class Country21Extractor(ILogger<Country21Extractor> logger) : IDataExtractor<Century21RawEntry>
{
    public async IAsyncEnumerable<Century21RawEntry> ExtractAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        var htmlContent = await reader.ReadToEndAsync(ct);

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        var wordBlocks = doc.DocumentNode.SelectNodes("//div[@class='word_block']");

        if (wordBlocks == null)
        {
            logger.LogWarning("No word blocks found in Country21 source");
            yield break;
        }

        foreach (var block in wordBlocks)
        {
            ct.ThrowIfCancellationRequested();

            var entry = ParseWordBlock(block);
            if (entry != null)
            {
                logger.LogDebug("Extracted entry: {Headword}", entry.Headword);
                yield return entry;
            }
        }
    }

    private Century21RawEntry? ParseWordBlock(HtmlNode block)
    {
        try
        {
            var headwordNode = block.SelectSingleNode(".//span[@class='headword']");
            if (headwordNode == null)
                return null;

            var headword = CleanText(headwordNode.InnerText);

            var phoneticsNode = block.SelectSingleNode(".//span[@class='phonetics']");
            var phonetics = phoneticsNode != null ? CleanText(phoneticsNode.InnerText) : null;

            var basicDef = block.SelectSingleNode(".//div[@class='basic_def']");
            var posNode = basicDef?.SelectSingleNode(".//span[@class='pos']");
            var pos = posNode != null ? CleanText(posNode.InnerText) : null;

            var definitionNode = basicDef?.SelectSingleNode(".//span[@class='definition']");
            var definition = definitionNode != null ? CleanText(definitionNode.InnerText) : null;

            var grammarNode = basicDef?.SelectSingleNode(".//span[@class='gram']");
            var grammarInfo = grammarNode != null ? CleanText(grammarNode.InnerText) : null;

            var examples = ExtractExamples(basicDef);

            var variants = ExtractVariants(block);

            var idioms = ExtractIdioms(block);

            if (string.IsNullOrWhiteSpace(headword) || string.IsNullOrWhiteSpace(definition))
                return null;

            return new Century21RawEntry
            {
                Headword = headword,
                Phonetics = phonetics,
                PartOfSpeech = pos,
                Definition = definition,
                GrammarInfo = grammarInfo,
                Examples = examples,
                Variants = variants,
                Idioms = idioms
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse word block");
            return null;
        }
    }

    private List<Country21Example> ExtractExamples(HtmlNode? container)
    {
        var examples = new List<Country21Example>();

        if (container == null)
            return examples;

        var exampleNodes = container.SelectNodes(".//div[@class='example']");
        if (exampleNodes == null)
            return examples;

        foreach (var exampleNode in exampleNodes)
        {
            var englishNode = exampleNode.SelectSingleNode(".//span[@class='ex_en']");
            var chineseNode = exampleNode.SelectSingleNode(".//span[@class='ex_cn']");

            if (englishNode != null)
            {
                examples.Add(new Country21Example
                {
                    English = CleanText(englishNode.InnerText),
                    Chinese = chineseNode != null ? CleanText(chineseNode.InnerText) : null
                });
            }
        }

        return examples;
    }

    private List<Country21Variant> ExtractVariants(HtmlNode block)
    {
        var variants = new List<Country21Variant>();

        var variantNodes = block.SelectNodes(".//div[@class='variant']//div[@class='item']");
        if (variantNodes == null)
            return variants;

        foreach (var variantNode in variantNodes)
        {
            var posNode = variantNode.SelectSingleNode(".//span[@class='pos']");
            var definitionNode = variantNode.SelectSingleNode(".//span[@class='definition']");

            if (definitionNode != null)
            {
                var variant = new Country21Variant
                {
                    PartOfSpeech = posNode != null ? CleanText(posNode.InnerText) : null,
                    Definition = CleanText(definitionNode.InnerText),
                    Examples = ExtractExamples(variantNode)
                };
                variants.Add(variant);
            }
        }

        return variants;
    }

    private List<Country21Idiom> ExtractIdioms(HtmlNode block)
    {
        var idioms = new List<Country21Idiom>();

        var idiomNodes = block.SelectNodes(".//div[@class='idiom']//div[@class='item']");
        if (idiomNodes == null)
            return idioms;

        foreach (var idiomNode in idiomNodes)
        {
            var headwordNode = idiomNode.SelectSingleNode(".//span[@class='headword']");
            var definitionNode = idiomNode.SelectSingleNode(".//span[@class='definition']");

            if (headwordNode != null && definitionNode != null)
            {
                var idiom = new Country21Idiom
                {
                    Headword = CleanText(headwordNode.InnerText),
                    Definition = CleanText(definitionNode.InnerText),
                    Examples = ExtractExamples(idiomNode)
                };
                idioms.Add(idiom);
            }
        }

        return idioms;
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = Regex.Replace(text, @"\s+", " ");
        text = text.Replace("&nbsp;", " ")
                  .Replace("&lt;", "<")
                  .Replace("&gt;", ">")
                  .Replace("&amp;", "&")
                  .Replace("&quot;", "\"")
                  .Replace("&apos;", "'");

        return text.Trim();
    }
}