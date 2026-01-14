using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordExtractor : IDataExtractor<OxfordRawEntry>
{
    public async IAsyncEnumerable<OxfordRawEntry> ExtractAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        OxfordRawEntry? currentEntry = null;
        OxfordSenseRaw? currentSense = null;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();

            line = line.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (OxfordParserHelper.IsEntrySeparator(line))
            {
                if (currentEntry != null)
                {
                    yield return currentEntry;
                    currentEntry = null;
                    currentSense = null;
                }

                continue;
            }

            if (OxfordParserHelper.TryParseHeadwordLine(line,
                    out var headword,
                    out var pronunciation,
                    out var partOfSpeech,
                    out var variantForms))
            {
                if (currentEntry != null)
                    yield return currentEntry;

                currentEntry = new OxfordRawEntry
                {
                    Headword = headword,
                    Pronunciation = pronunciation,
                    PartOfSpeech = partOfSpeech,
                    VariantForms = variantForms
                };
                currentSense = null;
                continue;
            }

            if (currentEntry == null)
                continue;

            if (OxfordParserHelper.TryParseSenseLine(line,
                    out var senseNumber,
                    out var senseLabel,
                    out var definition,
                    out var chineseTranslation))
            {
                if (currentSense != null)
                    currentEntry.Senses.Add(currentSense);

                currentSense = new OxfordSenseRaw
                {
                    SenseNumber = senseNumber,
                    SenseLabel = senseLabel,
                    Definition = definition,
                    ChineseTranslation = chineseTranslation
                };

                var crossRefs = OxfordParserHelper.ExtractCrossReferences(definition);
                foreach (var crossRef in crossRefs)
                    currentSense.CrossReferences.Add(crossRef);

                continue;
            }

            if (OxfordParserHelper.IsExampleLine(line))
            {
                var example = OxfordParserHelper.CleanExampleLine(line);
                if (!string.IsNullOrWhiteSpace(example) && currentSense != null)
                    currentSense.Examples.Add(example);
                continue;
            }

            if (currentSense != null &&
                !string.IsNullOrWhiteSpace(line) &&
                !line.StartsWith("【") && !line.StartsWith("◘"))
            {
                if (line.StartsWith("Usage", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                    currentSense.UsageNote = line;
                else
                    currentSense.Definition += " " + line;
            }
        }

        if (currentEntry != null)
        {
            if (currentSense != null)
                currentEntry.Senses.Add(currentSense);
            yield return currentEntry;
        }
    }
}