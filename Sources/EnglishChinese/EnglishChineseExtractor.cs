using System.Runtime.CompilerServices;
using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Sources.EnglishChinese.Models;

namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseExtractor
    : IDataExtractor<EnglishChineseRawEntry>
{
    private static readonly Regex HasEnglishLetter = new("[A-Za-z]", RegexOptions.Compiled);

    public async IAsyncEnumerable<EnglishChineseRawEntry> ExtractAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync();
            if (line == null)
                yield break;

            line = line.Trim();
            if (line.Length == 0)
                continue;

            // Rule 1: must contain separator
            var sepIndex = line.IndexOf('⬄');
            if (sepIndex <= 0)
                continue;

            var headword = line.Substring(0, sepIndex).Trim();

            // Rule 2: must contain English letters
            if (!HasEnglishLetter.IsMatch(headword))
                continue;

            // Rule 3: length guard (DB safety)
            if (headword.Length > 200)
                continue;

            yield return new EnglishChineseRawEntry
            {
                Headword = headword,
                RawLine = line
            };
        }
    }
}