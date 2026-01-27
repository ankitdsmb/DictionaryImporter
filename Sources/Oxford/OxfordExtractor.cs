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
        bool inExamplesSection = false;

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

                inExamplesSection = false;
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

                inExamplesSection = false;
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

                inExamplesSection = false;
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

                inExamplesSection = false;
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
                    inExamplesSection = true;
                }

                continue;
            }

            // ───────────────── STRUCTURED SECTION MARKER ─────────────────
            if (line.StartsWith("【", StringComparison.Ordinal))
            {
                // Examples, Usage, etc.
                if (currentSense != null)
                {
                    if (line.StartsWith("【Examples】", StringComparison.Ordinal))
                        inExamplesSection = true;
                    else
                        inExamplesSection = false;

                    // Append section marker to definition
                    if (string.IsNullOrWhiteSpace(currentSense.Definition))
                        currentSense.Definition = line;
                    else
                        currentSense.Definition += "\n" + line;
                }
                continue;
            }

            // ───────────────── CONTINUATION LINE ─────────────────
            if (currentSense != null)
            {
                if (inExamplesSection)
                {
                    // Continuation of example (multi-line example)
                    if (currentSense.Examples.Count > 0)
                    {
                        var lastExample = currentSense.Examples.Last();
                        currentSense.Examples[currentSense.Examples.Count - 1] =
                            lastExample + " " + line.Trim();
                    }
                }
                else if (line.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("Grammar:", StringComparison.OrdinalIgnoreCase))
                {
                    currentSense.UsageNote = line;
                }
                else if (line.StartsWith("IDIOM", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("PHRASAL VERB", StringComparison.OrdinalIgnoreCase))
                {
                    // Start of idioms or phrasal verbs subsection
                    if (!string.IsNullOrWhiteSpace(currentSense.Definition))
                        currentSense.Definition += "\n" + line;
                }
                else if (!line.StartsWith("◘"))
                {
                    // Regular continuation of definition
                    if (string.IsNullOrWhiteSpace(currentSense.Definition))
                        currentSense.Definition = line;
                    else
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