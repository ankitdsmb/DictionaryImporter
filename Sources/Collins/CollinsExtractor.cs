using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
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

                if (CollinsParsingHelper.IsEntrySeparator(line))
                {
                    if (currentEntry != null)
                    {
                        if (currentSense != null && examplesBuffer.Count > 0)
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }

                        // ✅ STRICT STOP
                        if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, null))
                            yield break;

                        yield return currentEntry;
                    }
                    currentEntry = null;
                    currentSense = null;
                    examplesBuffer.Clear();
                    noteBuffer.Clear();
                    continue;
                }

                if (CollinsParsingHelper.TryParseHeadword(line, out var headword))
                {
                    if (currentEntry != null)
                    {
                        if (currentSense != null && examplesBuffer.Count > 0)
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }

                        // ✅ STRICT STOP
                        if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, null))
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

                if (CollinsParsingHelper.TryParseSenseHeader(line, out var sense))
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

                if (CollinsParsingHelper.TryParseExample(line, out var example))
                {
                    examplesBuffer.Add(example);
                    continue;
                }

                if (CollinsParsingHelper.TryParseUsageNote(line, out var usageNote))
                {
                    noteBuffer.Add(usageNote);

                    if (currentSense != null)
                        currentSense.UsageNote = string.Join(" ", noteBuffer);

                    continue;
                }

                if (CollinsParsingHelper.TryParseDomainLabel(line, out var labelInfo))
                {
                    if (currentSense != null)
                    {
                        var cleanValue = CollinsParsingHelper.RemoveChineseCharacters(labelInfo.Value);

                        if (labelInfo.LabelType.Contains("语域"))
                            currentSense.DomainLabel = CollinsParsingHelper.ExtractCleanDomain(cleanValue);
                        else if (labelInfo.LabelType.Contains("语法"))
                            currentSense.GrammarInfo = CollinsParsingHelper.ExtractCleanGrammar(cleanValue);
                    }

                    continue;
                }

                if (currentSense != null &&
                    !string.IsNullOrEmpty(currentSense.Definition) &&
                    CollinsParsingHelper.IsDefinitionContinuation(line, currentSense.Definition))
                {
                    var cleanedLine = CollinsParsingHelper.RemoveChineseCharacters(line.Trim());
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
                if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, null))
                    yield break;

                yield return currentEntry;
            }
        }
    }
}