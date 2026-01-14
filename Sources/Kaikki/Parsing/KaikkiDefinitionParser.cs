using DictionaryImporter.Sources.Kaikki.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Sources.Kaikki.Parsing;

public sealed class KaikkiDefinitionParser(ILogger<KaikkiDefinitionParser> logger) : IDictionaryDefinitionParser
{
    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RawFragment))
        {
            return ParseFromDefinition(entry);
        }

        try
        {
            return ParseFromRawFragment(entry);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse Kaikki raw fragment for: {Word}", entry.Word);
            return ParseFromDefinition(entry);
        }
    }

    private IEnumerable<ParsedDefinition> ParseFromRawFragment(DictionaryEntry entry)
    {
        var parsingData = JsonConvert.DeserializeObject<JObject>(entry.RawFragment);
        if (parsingData == null)
        {
            logger.LogWarning("Failed to deserialize Kaikki raw fragment for: {Word}", entry.Word);
            return ParseFromDefinition(entry);
        }

        var word = parsingData["Word"]?.Value<string>() ?? entry.Word;
        var partOfSpeech = parsingData["PartOfSpeech"]?.Value<string>();
        var senseNumber = parsingData["SenseNumber"]?.Value<int>() ?? entry.SenseNumber;

        var senseToken = parsingData["Sense"];
        if (senseToken == null)
        {
            return ParseFromDefinition(entry);
        }

        var sense = senseToken.ToObject<KaikkiSense>();
        if (sense == null)
        {
            return ParseFromDefinition(entry);
        }

        var synonyms = ExtractSynonyms(parsingData);

        var crossRefs = ExtractCrossReferences(parsingData);

        var examples = ExtractExamples(sense);

        var definition = sense.Glosses != null && sense.Glosses.Count > 0
            ? string.Join("; ", sense.Glosses.Select(CleanGloss))
            : entry.Definition;

        var parsedDefinition = new ParsedDefinition
        {
            MeaningTitle = word,
            Definition = definition,
            RawFragment = entry.RawFragment,
            SenseNumber = senseNumber,
            Domain = ExtractDomain(sense),
            UsageLabel = ExtractUsageLabel(sense),
            CrossReferences = crossRefs,
            Synonyms = synonyms,
            Alias = ExtractAlias(parsingData)
        };

        if (examples.Count > 0)
        {
            parsedDefinition.Examples = examples;

            if (!string.IsNullOrWhiteSpace(parsedDefinition.Definition))
            {
                parsedDefinition.Definition += "\n【Examples】";
                foreach (var example in examples.Take(3))
                {
                    parsedDefinition.Definition += $"\n• {example}";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(partOfSpeech))
        {
            parsedDefinition.PartOfSpeech = NormalizePartOfSpeech(partOfSpeech);
        }

        return new List<ParsedDefinition> { parsedDefinition };
    }

    private IEnumerable<ParsedDefinition> ParseFromDefinition(DictionaryEntry entry)
    {
        var parsed = new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = entry.Definition ?? string.Empty,
            RawFragment = entry.RawFragment ?? string.Empty,
            SenseNumber = entry.SenseNumber,
            Domain = ExtractDomainFromText(entry.Definition),
            UsageLabel = ExtractUsageLabelFromText(entry.Definition),
            CrossReferences = ExtractCrossReferencesFromText(entry.Definition),
            Synonyms = ExtractSynonymsFromText(entry.Definition),
            Alias = null
        };

        var examples = ExtractExamplesFromText(entry.Definition);
        if (examples.Count > 0)
        {
            parsed.Examples = examples;
        }

        return new List<ParsedDefinition> { parsed };
    }

    private List<string> ExtractExamples(KaikkiSense sense)
    {
        var examples = new List<string>();

        if (sense.Examples == null)
            return examples;

        foreach (var example in sense.Examples)
        {
            if (!string.IsNullOrWhiteSpace(example.Text))
            {
                var cleaned = CleanExampleText(example.Text);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    examples.Add(cleaned);
                }
            }
        }

        return examples;
    }

    private List<string> ExtractExamplesFromText(string? definition)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(definition))
            return examples;

        var lines = definition.Split('\n');
        var inExamplesSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("【Examples】"))
            {
                inExamplesSection = true;
                continue;
            }

            if (inExamplesSection)
            {
                if (trimmed.StartsWith("【") || string.IsNullOrEmpty(trimmed))
                {
                    break;
                }

                if (trimmed.StartsWith("•"))
                {
                    var example = trimmed.Substring(1).Trim();
                    if (!string.IsNullOrWhiteSpace(example))
                    {
                        examples.Add(example);
                    }
                }
            }
        }

        return examples;
    }

    private List<string>? ExtractSynonyms(JObject parsingData)
    {
        var synonyms = new List<string>();

        var synonymsToken = parsingData["Synonyms"];
        if (synonymsToken != null)
        {
            var synonymsList = synonymsToken.ToObject<List<KaikkiSynonym>>();
            if (synonymsList != null)
            {
                foreach (var synonym in synonymsList)
                {
                    if (!string.IsNullOrWhiteSpace(synonym.Word))
                    {
                        synonyms.Add(synonym.Word.ToLowerInvariant());
                    }
                }
            }
        }

        return synonyms.Count > 0 ? synonyms.Distinct().ToList() : null;
    }

    private List<string>? ExtractSynonymsFromText(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        var synonyms = new List<string>();

        var lines = definition.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("【Synonyms】"))
            {
                var synonymsText = line["【Synonyms】".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(synonymsText))
                {
                    var words = synonymsText.Split(',');
                    foreach (var word in words)
                    {
                        var trimmed = word.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            synonyms.Add(trimmed.ToLowerInvariant());
                        }
                    }
                }
                break;
            }
        }

        return synonyms.Count > 0 ? synonyms : null;
    }

    private List<CrossReference> ExtractCrossReferences(JObject parsingData)
    {
        var crossRefs = new List<CrossReference>();

        var derivedToken = parsingData["Derived"];
        if (derivedToken != null)
        {
            var derivedList = derivedToken.ToObject<List<KaikkiDerived>>();
            if (derivedList != null)
            {
                foreach (var derived in derivedList)
                {
                    if (!string.IsNullOrWhiteSpace(derived.Word))
                    {
                        crossRefs.Add(new CrossReference
                        {
                            TargetWord = derived.Word,
                            ReferenceType = "Derived"
                        });
                    }
                }
            }
        }

        return crossRefs;
    }

    private List<CrossReference> ExtractCrossReferencesFromText(string? definition)
    {
        var crossRefs = new List<CrossReference>();

        if (string.IsNullOrWhiteSpace(definition))
            return crossRefs;

        var lines = definition.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("【Derived】"))
            {
                var derivedText = line["【Derived】".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(derivedText))
                {
                    var words = derivedText.Split(',');
                    foreach (var word in words)
                    {
                        var trimmed = word.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            crossRefs.Add(new CrossReference
                            {
                                TargetWord = trimmed,
                                ReferenceType = "Derived"
                            });
                        }
                    }
                }
                break;
            }
        }

        return crossRefs;
    }

    private string? ExtractDomain(KaikkiSense sense)
    {
        if (sense.Categories != null && sense.Categories.Count > 0)
        {
            foreach (var category in sense.Categories)
            {
                if (!string.IsNullOrWhiteSpace(category))
                {
                    var cleaned = CleanGloss(category);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        return cleaned;
                    }
                }
            }
        }

        return null;
    }

    private string? ExtractDomainFromText(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        var lines = definition.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("【Categories】"))
            {
                var categories = line["【Categories】".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(categories))
                {
                    var firstCategory = categories.Split(',').FirstOrDefault()?.Trim();
                    return firstCategory;
                }
            }
            else if (line.StartsWith("【Topics】"))
            {
                var topics = line["【Topics】".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(topics))
                {
                    var firstTopic = topics.Split(',').FirstOrDefault()?.Trim();
                    return firstTopic;
                }
            }
        }

        return null;
    }

    private string? ExtractUsageLabel(KaikkiSense sense)
    {
        if (sense.Tags != null && sense.Tags.Count > 0)
        {
            foreach (var tag in sense.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    var lowerTag = tag.ToLowerInvariant();
                    if (lowerTag.Contains("formal") || lowerTag.Contains("informal") ||
                        lowerTag.Contains("slang") || lowerTag.Contains("archaic") ||
                        lowerTag.Contains("literary") || lowerTag.Contains("technical"))
                    {
                        return tag;
                    }
                }
            }
        }

        return null;
    }

    private string? ExtractUsageLabelFromText(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        var lines = definition.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("【Tags】"))
            {
                var tags = line["【Tags】".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(tags))
                {
                    var tagList = tags.Split(',');
                    foreach (var tag in tagList)
                    {
                        var trimmed = tag.Trim().ToLowerInvariant();
                        if (trimmed.Contains("formal") || trimmed.Contains("informal") ||
                            trimmed.Contains("slang") || trimmed.Contains("archaic") ||
                            trimmed.Contains("literary") || trimmed.Contains("technical"))
                        {
                            return tag.Trim();
                        }
                    }
                }
                break;
            }
        }

        return null;
    }

    private string? ExtractAlias(JObject parsingData)
    {
        return null;
    }

    private string CleanGloss(string gloss)
    {
        if (string.IsNullOrWhiteSpace(gloss))
            return string.Empty;

        gloss = Regex.Replace(gloss, @"\{\{.*?\}\}", string.Empty);
        gloss = Regex.Replace(gloss, @"\[\[.*?\]\]", string.Empty);
        gloss = Regex.Replace(gloss, @"'''(.*?)'''", "$1");
        gloss = Regex.Replace(gloss, @"''(.*?)''", "$1");

        gloss = gloss.Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&amp;", "&")
                    .Replace("&quot;", "\"");

        return gloss.Trim();
    }

    private string CleanExampleText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = CleanGloss(text);

        text = text.Trim();
        if (!text.EndsWith(".") && !text.EndsWith("!") && !text.EndsWith("?"))
        {
            text += ".";
        }

        if (text.Length > 0 && char.IsLower(text[0]))
        {
            text = char.ToUpper(text[0]) + text.Substring(1);
        }

        return text;
    }

    private string? NormalizePartOfSpeech(string? pos)
    {
        if (string.IsNullOrWhiteSpace(pos))
            return null;

        var normalized = pos.Trim().ToLowerInvariant();

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
            _ => normalized
        };
    }
}