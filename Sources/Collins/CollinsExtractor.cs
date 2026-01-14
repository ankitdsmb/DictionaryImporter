using DictionaryImporter.Sources.Collins.parsing;

namespace DictionaryImporter.Sources.Collins;

public sealed class CollinsExtractor : IDataExtractor<CollinsRawEntry>
{
    public async IAsyncEnumerable<CollinsRawEntry> ExtractAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
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
                if (currentSense != null && examplesBuffer.Any())
                {
                    currentSense.Examples.AddRange(examplesBuffer);
                    examplesBuffer.Clear();
                }

                continue;
            }

            if (CollinsParserHelper.IsEntrySeparator(line))
            {
                if (currentEntry != null)
                {
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

            if (CollinsParserHelper.TryParseHeadword(line, out var headword))
            {
                if (currentEntry != null)
                {
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

            if (CollinsParserHelper.TryParseSenseHeader(line, out var sense))
            {
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

            if (CollinsParserHelper.TryParseExample(line, out var example))
            {
                examplesBuffer.Add(example);
                continue;
            }

            if (CollinsParserHelper.TryParseUsageNote(line, out var usageNote))
            {
                noteBuffer.Add(usageNote);
                if (currentSense != null) currentSense.UsageNote = string.Join(" ", noteBuffer);
                continue;
            }

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

            if (currentSense != null &&
                !string.IsNullOrEmpty(currentSense.Definition) &&
                CollinsParserHelper.IsDefinitionContinuation(line, currentSense.Definition))
            {
                var cleanedLine = CollinsParserHelper.RemoveChineseCharacters(line.Trim());
                currentSense.Definition += " " + cleanedLine;
            }
        }

        if (currentEntry != null)
        {
            if (currentSense != null)
            {
                if (examplesBuffer.Any()) currentSense.Examples.AddRange(examplesBuffer);
                currentEntry.Senses.Add(currentSense);
            }

            yield return currentEntry;
        }
    }
}