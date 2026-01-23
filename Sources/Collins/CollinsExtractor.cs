using DictionaryImporter.Common;
using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Collins
{
    public sealed class CollinsExtractor : IDataExtractor<CollinsRawEntry>
    {
        private const string SourceCode = "ENG_COLLINS";

        public async IAsyncEnumerable<CollinsRawEntry> ExtractAsync(
    Stream stream, [EnumeratorCancellation] CancellationToken ct)
        {
            using var reader = new StreamReader(stream);
            CollinsRawEntry? currentEntry = null;
            CollinsSenseRaw? currentSense = null;
            var examplesBuffer = new List<string>();
            var noteBuffer = new List<string>();

            while (await reader.ReadLineAsync() is { } line)
            {
                ct.ThrowIfCancellationRequested();
                line = line.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentSense != null && examplesBuffer.Count > 0)
                    {
                        currentSense.Examples.AddRange(examplesBuffer);
                        examplesBuffer.Clear();
                    }
                    continue;
                }

                if (ParsingHelperCollins.IsEntrySeparator(line))
                {
                    if (currentEntry != null)
                    {
                        if (currentSense != null && examplesBuffer.Count > 0)
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }

                        // ✅ STRICT STOP
                        if (!Helper.ShouldContinueProcessing(SourceCode, null))
                            yield break;

                        yield return currentEntry;
                    }
                    currentEntry = null;
                    currentSense = null;
                    examplesBuffer.Clear();
                    noteBuffer.Clear();
                    continue;
                }

                if (ParsingHelperCollins.TryParseHeadword(line, out var headword))
                {
                    if (currentEntry != null)
                    {
                        if (currentSense != null && examplesBuffer.Count > 0)
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }

                        // ✅ STRICT STOP
                        if (!Helper.ShouldContinueProcessing(SourceCode, null))
                            yield break;

                        yield return currentEntry;
                    }

                    currentEntry = new CollinsRawEntry { Headword = headword };
                    currentSense = null;
                    examplesBuffer.Clear();
                    noteBuffer.Clear();
                    continue;
                }

                if (currentEntry == null)
                    continue;

                if (ParsingHelperCollins.TryParseSenseHeader(line, out var sense))
                {
                    if (currentSense != null)
                    {
                        if (examplesBuffer.Count > 0)
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }
                        currentEntry.Senses.Add(currentSense);
                    }

                    currentSense = sense;
                    noteBuffer.Clear();
                    continue;
                }

                if (ParsingHelperCollins.TryParseExample(line, out var example))
                {
                    examplesBuffer.Add(example);
                    continue;
                }

                if (ParsingHelperCollins.TryParseUsageNote(line, out var usageNote))
                {
                    noteBuffer.Add(usageNote);

                    if (currentSense != null)
                        currentSense.UsageNote = string.Join(" ", noteBuffer);

                    continue;
                }

                if (ParsingHelperCollins.TryParseDomainLabel(line, out var labelInfo))
                {
                    if (currentSense != null)
                    {
                        var cleanValue = ParsingHelperCollins.RemoveChineseCharacters(labelInfo.Value);

                        if (labelInfo.LabelType.Contains("语域"))
                            currentSense.DomainLabel = ParsingHelperCollins.ExtractCleanDomain(cleanValue);
                        else if (labelInfo.LabelType.Contains("语法"))
                            currentSense.GrammarInfo = ParsingHelperCollins.ExtractCleanGrammar(cleanValue);
                    }

                    continue;
                }

                if (currentSense != null &&
                    !string.IsNullOrEmpty(currentSense.Definition) &&
                    ParsingHelperCollins.IsDefinitionContinuation(line, currentSense.Definition))
                {
                    var cleanedLine = ParsingHelperCollins.RemoveChineseCharacters(line.Trim());
                    currentSense.Definition += " " + cleanedLine;
                }
            }

            if (currentEntry != null)
            {
                if (currentSense != null)
                {
                    if (examplesBuffer.Count > 0)
                        currentSense.Examples.AddRange(examplesBuffer);
                    currentEntry.Senses.Add(currentSense);
                }

                // ✅ STRICT STOP
                if (!Helper.ShouldContinueProcessing(SourceCode, null))
                    yield break;

                yield return currentEntry;
            }
        }
    }
}