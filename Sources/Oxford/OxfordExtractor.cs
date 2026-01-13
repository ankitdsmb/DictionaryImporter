// DictionaryImporter/Sources/Oxford/OxfordExtractor.cs

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

            // Check for entry separator
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

            // Try to parse as headword line
            if (OxfordParserHelper.TryParseHeadwordLine(line,
                    out var headword,
                    out var pronunciation,
                    out var partOfSpeech,
                    out var variantForms))
            {
                // Save previous entry if exists
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

            // If we don't have an entry yet, skip
            if (currentEntry == null)
                continue;

            // Try to parse as sense line
            if (OxfordParserHelper.TryParseSenseLine(line,
                    out var senseNumber,
                    out var senseLabel,
                    out var definition,
                    out var chineseTranslation))
            {
                // Save previous sense if exists
                if (currentSense != null)
                    currentEntry.Senses.Add(currentSense);

                currentSense = new OxfordSenseRaw
                {
                    SenseNumber = senseNumber,
                    SenseLabel = senseLabel,
                    Definition = definition,
                    ChineseTranslation = chineseTranslation
                };

                // Extract cross-references from definition
                var crossRefs = OxfordParserHelper.ExtractCrossReferences(definition);
                foreach (var crossRef in crossRefs)
                    currentSense.CrossReferences.Add(crossRef);

                continue;
            }

            // Check for example line
            if (OxfordParserHelper.IsExampleLine(line))
            {
                var example = OxfordParserHelper.CleanExampleLine(line);
                if (!string.IsNullOrWhiteSpace(example) && currentSense != null)
                    currentSense.Examples.Add(example);
                continue;
            }

            // Check for continuation lines (might be part of definition or usage note)
            if (currentSense != null &&
                !string.IsNullOrWhiteSpace(line) &&
                !line.StartsWith("【") && // Not a section marker
                !line.StartsWith("◘")) // Not an idiom marker
            {
                // Could be continuation of definition or a usage note
                if (line.StartsWith("Usage", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                    currentSense.UsageNote = line;
                else
                    // Append to definition
                    currentSense.Definition += " " + line;
            }
        }

        // Yield the last entry
        if (currentEntry != null)
        {
            if (currentSense != null)
                currentEntry.Senses.Add(currentSense);
            yield return currentEntry;
        }
    }
}