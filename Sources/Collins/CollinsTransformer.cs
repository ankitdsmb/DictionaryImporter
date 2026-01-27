using DictionaryImporter.Common;

namespace DictionaryImporter.Sources.Collins;

public sealed class CollinsTransformer(ILogger<CollinsTransformer> logger)
    : IDataTransformer<CollinsRawEntry>
{
    private const string SourceCode = "ENG_COLLINS";

    public IEnumerable<DictionaryEntry> Transform(CollinsRawEntry? raw)
    {
        if (!Helper.ShouldContinueProcessing(SourceCode, logger))
            yield break;

        if (raw == null || !raw.Senses.Any())
            yield break;

        foreach (var entry in ProcessCollinsEntry(raw))
            yield return entry;
    }

    private IEnumerable<DictionaryEntry> ProcessCollinsEntry(CollinsRawEntry raw)
    {
        var entries = new List<DictionaryEntry>();

        try
        {
            var normalizedWord = Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

            foreach (var sense in raw.Senses)
            {
                var fullDefinition = BuildFullDefinition(sense);
                var rawFragment = BuildRawFragment(sense);

                // Handle cross-references specially
                if (sense.PartOfSpeech == "ref" && sense.Definition?.Contains("→see:") == true)
                {
                    var crossRefMatch = Regex.Match(sense.Definition, @"→see:\s*(.+)");
                    if (crossRefMatch.Success)
                    {
                        fullDefinition = $"See: {crossRefMatch.Groups[1].Value}";
                        rawFragment = fullDefinition;
                    }
                }

                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = CollinsExtractor.NormalizePos(sense.PartOfSpeech),
                    Definition = fullDefinition,
                    RawFragment = rawFragment,
                    SenseNumber = sense.SenseNumber,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow,
                    Examples = sense.Examples?.ToList() ?? new List<string>(),
                    UsageNote = sense.UsageNote,
                    DomainLabel = sense.DomainLabel,
                    GrammarInfo = sense.GrammarInfo
                });
            }

            Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));
        }
        catch (Exception ex)
        {
            Helper.HandleError(logger, ex, SourceCode, "transforming");
        }

        foreach (var entry in entries)
            yield return entry;
    }

    private static string BuildFullDefinition(CollinsSenseRaw sense)
    {
        var parts = new List<string>();

        // Add sense header
        parts.Add($"{sense.SenseNumber}.{sense.PartOfSpeech.ToUpper()}");

        // Add main definition (ensure it's cleaned)
        if (!string.IsNullOrEmpty(sense.Definition))
        {
            var cleanedDef = CollinsExtractor.RemoveChineseCharacters(sense.Definition)
                .Replace(" ; ; ", " ")
                .Replace(" ; ", " ")
                .Replace("  ", " ")
                .Trim();

            // Ensure it ends with punctuation
            if (!string.IsNullOrEmpty(cleanedDef) &&
                !cleanedDef.EndsWith(".") &&
                !cleanedDef.EndsWith("!") &&
                !cleanedDef.EndsWith("?") &&
                !cleanedDef.Contains("→see:"))
            {
                cleanedDef += ".";
            }

            parts.Add(cleanedDef);
        }

        // Add grammar info
        if (!string.IsNullOrEmpty(sense.GrammarInfo))
        {
            var cleanGrammar = CollinsExtractor.RemoveChineseCharacters(sense.GrammarInfo);
            if (!string.IsNullOrWhiteSpace(cleanGrammar))
            {
                parts.Add($"【Grammar】{cleanGrammar}");
            }
        }

        // Add domain label
        if (!string.IsNullOrEmpty(sense.DomainLabel))
        {
            parts.Add($"【Domain】{sense.DomainLabel}");
        }

        // Add usage note
        if (!string.IsNullOrEmpty(sense.UsageNote))
        {
            var cleanNote = CollinsExtractor.RemoveChineseCharacters(sense.UsageNote);
            if (!string.IsNullOrWhiteSpace(cleanNote))
            {
                parts.Add($"【Note】{cleanNote}");
            }
        }

        // Add examples
        if (sense.Examples.Any())
        {
            parts.Add("【Examples】");
            foreach (var example in sense.Examples)
            {
                var cleanExample = CollinsExtractor.RemoveChineseCharacters(example)
                    .Trim();
                if (!string.IsNullOrWhiteSpace(cleanExample))
                {
                    parts.Add($"• {cleanExample}");
                }
            }
        }

        return string.Join("\n", parts);
    }

    private static string BuildRawFragment(CollinsSenseRaw sense)
    {
        // For cross-references, keep it simple
        if (sense.PartOfSpeech == "ref" && sense.Definition?.Contains("→see:") == true)
        {
            return sense.Definition;
        }

        var parts = new List<string>();

        // Add sense header
        parts.Add($"{sense.SenseNumber}.{sense.PartOfSpeech.ToUpper()}");

        // Add definition
        if (!string.IsNullOrEmpty(sense.Definition))
        {
            parts.Add(sense.Definition);
        }

        return string.Join(" ", parts).Trim();
    }
}