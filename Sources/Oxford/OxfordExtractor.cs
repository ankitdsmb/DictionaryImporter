using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford
{
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

                if (OxfordSourceDataHelper.IsEntrySeparator(line))
                {
                    if (currentEntry != null)
                    {
                        // flush current sense before yielding entry
                        if (currentSense != null)
                        {
                            currentEntry.Senses.Add(currentSense);
                            currentSense = null;
                        }

                        // ✅ STRICT: stop reading file once limit reached
                        if (!Helper.ShouldContinueProcessing(SourceCode, null))
                            yield break;

                        yield return currentEntry;
                        currentEntry = null;
                    }

                    continue;
                }

                if (OxfordSourceDataHelper.TryParseHeadwordLine(
                        line,
                        out var headword,
                        out var pronunciation,
                        out var partOfSpeech,
                        out var variantForms))
                {
                    // flush old entry (with last pending sense)
                    if (currentEntry != null)
                    {
                        if (currentSense != null)
                        {
                            currentEntry.Senses.Add(currentSense);
                            currentSense = null;
                        }

                        // ✅ STRICT: stop reading file once limit reached
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

                    currentSense = null;
                    continue;
                }

                if (currentEntry == null)
                    continue;

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
                        SenseLabel = senseLabel,
                        Definition = definition,
                        ChineseTranslation = chineseTranslation
                    };

                    var crossRefs = OxfordSourceDataHelper.ExtractCrossReferences(definition);
                    foreach (var crossRef in crossRefs)
                        currentSense.CrossReferences.Add(crossRef);

                    continue;
                }

                if (OxfordSourceDataHelper.IsExampleLine(line))
                {
                    var example = OxfordSourceDataHelper.CleanExampleLine(line);

                    if (!string.IsNullOrWhiteSpace(example) && currentSense != null)
                        currentSense.Examples.Add(example);

                    continue;
                }

                if (currentSense != null &&
                    !string.IsNullOrWhiteSpace(line) &&
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

            // flush last entry at EOF
            if (currentEntry != null)
            {
                if (currentSense != null)
                    currentEntry.Senses.Add(currentSense);

                // ✅ STRICT: stop reading file once limit reached
                if (!Helper.ShouldContinueProcessing(SourceCode, null))
                    yield break;

                yield return currentEntry;
            }
        }
    }
}