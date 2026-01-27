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
        bool inIdiomsSection = false;
        bool inEtymologySection = false;
        bool inDerivedSection = false;

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
                inIdiomsSection = false;
                inEtymologySection = false;
                inDerivedSection = false;
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
                inIdiomsSection = false;
                inEtymologySection = false;
                inDerivedSection = false;
                continue;
            }

            if (currentEntry == null)
                continue;

            // ───────────────── STRUCTURED SECTION MARKERS ─────────────────
            if (line.StartsWith("【", StringComparison.Ordinal))
            {
                // Reset all section flags
                inExamplesSection = false;
                inIdiomsSection = false;
                inEtymologySection = false;
                inDerivedSection = false;

                // Set appropriate section flag
                if (line.StartsWith("【Examples】", StringComparison.OrdinalIgnoreCase))
                    inExamplesSection = true;
                else if (line.StartsWith("【IDIOMS】", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("【PHR V】", StringComparison.OrdinalIgnoreCase))
                    inIdiomsSection = true;
                else if (line.StartsWith("【语源】", StringComparison.OrdinalIgnoreCase))
                    inEtymologySection = true;
                else if (line.StartsWith("【派生】", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("【用法】", StringComparison.OrdinalIgnoreCase))
                    inDerivedSection = true;

                // Append section marker to current sense definition
                if (currentSense != null)
                {
                    if (string.IsNullOrWhiteSpace(currentSense.Definition))
                        currentSense.Definition = line;
                    else
                        currentSense.Definition += "\n" + line;
                }
                continue;
            }

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
                inIdiomsSection = false;
                inEtymologySection = false;
                inDerivedSection = false;
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

                // Extract cross-references from definition
                var crossRefs = OxfordSourceDataHelper.ExtractCrossReferences(definition);
                foreach (var crossRef in crossRefs)
                    currentSense.CrossReferences.Add(crossRef);

                inExamplesSection = false;
                inIdiomsSection = false;
                inEtymologySection = false;
                inDerivedSection = false;
                continue;
            }

            // ───────────────── EXAMPLE LINE ─────────────────
            if (OxfordSourceDataHelper.IsExampleLine(line))
            {
                if (currentSense != null)
                {
                    var example = OxfordSourceDataHelper.CleanExampleLine(line);
                    if (!string.IsNullOrWhiteSpace(example) && example.Length > 5)
                        currentSense.Examples.Add(example);
                }
                continue;
            }

            // ───────────────── CONTINUATION LINE ─────────────────
            if (currentSense != null)
            {
                if (inExamplesSection && OxfordSourceDataHelper.IsExampleLine(line))
                {
                    // Example line within Examples section
                    var example = OxfordSourceDataHelper.CleanExampleLine(line);
                    if (!string.IsNullOrWhiteSpace(example) && example.Length > 5)
                        currentSense.Examples.Add(example);
                }
                else if (inIdiomsSection)
                {
                    // Idioms or phrasal verbs
                    if (string.IsNullOrWhiteSpace(currentSense.Definition))
                        currentSense.Definition = line;
                    else
                        currentSense.Definition += "\n" + line;
                }
                else if (inEtymologySection)
                {
                    // Etymology text
                    if (string.IsNullOrWhiteSpace(currentSense.Definition))
                        currentSense.Definition = line;
                    else
                        currentSense.Definition += "\n" + line;
                }
                else if (inDerivedSection)
                {
                    // Derived forms or usage notes
                    if (string.IsNullOrWhiteSpace(currentSense.Definition))
                        currentSense.Definition = line;
                    else
                        currentSense.Definition += "\n" + line;
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
                else if (!line.StartsWith("◘") && !string.IsNullOrWhiteSpace(line))
                {
                    // Regular continuation of definition (multi-line definition)
                    // Clean Chinese text from continuation lines
                    var cleanLine = Regex.Replace(line, @"[\u4e00-\u9fff].*$", "").Trim();
                    cleanLine = Regex.Replace(cleanLine, @"•\s*[\u4e00-\u9fff].*$", "").Trim();

                    if (!string.IsNullOrWhiteSpace(cleanLine))
                    {
                        if (string.IsNullOrWhiteSpace(currentSense.Definition))
                            currentSense.Definition = cleanLine;
                        else
                            currentSense.Definition += " " + cleanLine;
                    }
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