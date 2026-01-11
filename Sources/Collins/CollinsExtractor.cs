using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Sources.Collins.Models;
using DictionaryImporter.Sources.Collins.Parsing;

namespace DictionaryImporter.Sources.Collins
{
    public sealed class CollinsExtractor : IDataExtractor<CollinsRawEntry>
    {
        public async IAsyncEnumerable<CollinsRawEntry> ExtractAsync(
            Stream stream,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            using var reader = new StreamReader(stream);

            CollinsRawEntry? currentEntry = null;
            CollinsSenseRaw? currentSense = null;
            var examplesBuffer = new List<string>();
            var noteBuffer = new List<string>();

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();

                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Empty line might separate senses
                    if (currentSense != null && examplesBuffer.Any())
                    {
                        currentSense.Examples.AddRange(examplesBuffer);
                        examplesBuffer.Clear();
                    }
                    continue;
                }

                // Check for entry separator
                if (CollinsParserHelper.IsEntrySeparator(line))
                {
                    if (currentEntry != null)
                    {
                        // Finalize current sense
                        if (currentSense != null && examplesBuffer.Any())
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }

                        yield return currentEntry;
                    }

                    currentEntry = null;
                    currentSense = null;
                    continue;
                }

                // Parse headword
                if (CollinsParserHelper.TryParseHeadword(line, out var headword))
                {
                    if (currentEntry != null)
                    {
                        // Finalize previous entry
                        if (currentSense != null && examplesBuffer.Any())
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }

                        yield return currentEntry;
                    }

                    currentEntry = new CollinsRawEntry
                    {
                        Headword = headword
                    };
                    continue;
                }

                if (currentEntry == null)
                    continue;

                // Parse sense header
                if (CollinsParserHelper.TryParseSenseHeader(line, out var sense))
                {
                    // Save previous sense if exists
                    if (currentSense != null)
                    {
                        if (examplesBuffer.Any())
                        {
                            currentSense.Examples.AddRange(examplesBuffer);
                            examplesBuffer.Clear();
                        }
                        currentEntry.Senses.Add(currentSense);
                    }

                    currentSense = sense;
                    continue;
                }

                // Parse examples
                if (CollinsParserHelper.TryParseExample(line, out var example))
                {
                    examplesBuffer.Add(example);
                    continue;
                }

                // Parse usage notes
                if (CollinsParserHelper.TryParseUsageNote(line, out var usageNote))
                {
                    noteBuffer.Add(usageNote);
                    if (currentSense != null)
                    {
                        currentSense.UsageNote = string.Join(" ", noteBuffer);
                    }
                    continue;
                }

                // Parse domain/grammar labels
                if (CollinsParserHelper.TryParseDomainLabel(line, out var labelInfo))
                {
                    if (currentSense != null)
                    {
                        var cleanValue = CollinsParserHelper.RemoveChineseCharacters(labelInfo.Value);
                        if (labelInfo.LabelType.Contains("语域"))
                            currentSense.DomainLabel = CollinsParserHelper.ExtractCleanDomain(cleanValue);
                        else if (labelInfo.LabelType.Contains("语法"))
                            currentSense.GrammarInfo = CollinsParserHelper.ExtractCleanGrammar(cleanValue);
                    }
                    continue;
                }

                // Handle continuation of definition (multi-line definitions)
                if (currentSense != null &&
                    !string.IsNullOrEmpty(currentSense.Definition) &&
                    CollinsParserHelper.IsDefinitionContinuation(line, currentSense.Definition))
                {
                    var cleanedLine = CollinsParserHelper.RemoveChineseCharacters(line.Trim());
                    currentSense.Definition += " " + cleanedLine;
                }
            }

            // Yield the last entry
            if (currentEntry != null)
            {
                if (currentSense != null)
                {
                    if (examplesBuffer.Any())
                    {
                        currentSense.Examples.AddRange(examplesBuffer);
                    }
                    currentEntry.Senses.Add(currentSense);
                }
                yield return currentEntry;
            }
        }
    }
}