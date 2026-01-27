using DictionaryImporter.Common;
using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordExtractor : IDataExtractor<OxfordRawEntry>
{
    private const string SourceCode = "ENG_OXFORD";

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

            // ───────────────── ENTRY SEPARATOR ─────────────────
            if (OxfordSourceDataHelper.IsEntrySeparator(line))
            {
                if (currentSense != null && currentEntry != null)
                {
                    currentEntry.Senses.Add(currentSense);
                    currentSense = null;
                }

                if (currentEntry != null)
                {
                    if (!Helper.ShouldContinueProcessing(SourceCode, null))
                        yield break;

                    yield return currentEntry;
                    currentEntry = null;
                }

                continue;
            }

            // ───────────────── HEADWORD LINE ─────────────────
            if (OxfordSourceDataHelper.TryParseHeadwordLine(
                    line,
                    out var headword,
                    out var pronunciation,
                    out var partOfSpeech,
                    out var variantForms))
            {
                if (currentSense != null && currentEntry != null)
                {
                    currentEntry.Senses.Add(currentSense);
                    currentSense = null;
                }

                if (currentEntry != null)
                {
                    if (!Helper.ShouldContinueProcessing(SourceCode, null))
                        yield break;

                    yield return currentEntry;
                }

                currentEntry = new OxfordRawEntry
                {
                    Headword = headword,
                    Pronunciation = pronunciation,
                    PartOfSpeech = partOfSpeech,
                    VariantForms = variantForms
                };

                continue;
            }

            if (currentEntry == null)
                continue;

            // ───────────────── POS BLOCK HEADER ─────────────────
            if (line.StartsWith("▶", StringComparison.Ordinal))
            {
                if (currentSense != null)
                {
                    currentEntry.Senses.Add(currentSense);
                    currentSense = null;
                }

                currentSense = new OxfordSenseRaw
                {
                    SenseLabel = line.TrimStart('▶', ' ').Trim()
                };

                continue;
            }

            // ───────────────── NUMBERED SENSE ─────────────────
            if (OxfordSourceDataHelper.TryParseSenseLine(
                    line,
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
                    SenseLabel = senseLabel ?? currentSense?.SenseLabel,
                    Definition = definition,
                    ChineseTranslation = chineseTranslation
                };

                var crossRefs = OxfordSourceDataHelper.ExtractCrossReferences(definition);
                foreach (var crossRef in crossRefs)
                    currentSense.CrossReferences.Add(crossRef);

                continue;
            }

            // ───────────────── EXAMPLE LINE ─────────────────
            if (OxfordSourceDataHelper.IsExampleLine(line))
            {
                if (currentSense != null)
                {
                    var example = OxfordSourceDataHelper.CleanExampleLine(line);
                    if (!string.IsNullOrWhiteSpace(example))
                        currentSense.Examples.Add(example);
                }

                continue;
            }

            // ───────────────── CONTINUATION ─────────────────
            if (currentSense != null &&
                !line.StartsWith("【") &&
                !line.StartsWith("◘"))
            {
                if (line.StartsWith("Usage", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                {
                    currentSense.UsageNote = line;
                }
                else
                {
                    currentSense.Definition += " " + line;
                }
            }
        }

        // ───────────────── EOF FLUSH ─────────────────
        if (currentSense != null && currentEntry != null)
        {
            currentEntry.Senses.Add(currentSense);
            currentSense = null;
        }

        if (currentEntry != null)
        {
            if (!Helper.ShouldContinueProcessing(SourceCode, null))
                yield break;

            yield return currentEntry;
        }
    }
}