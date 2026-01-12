using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Sources.EnglishChinese.Models;

namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseTransformer
    : IDataTransformer<EnglishChineseRawEntry>
{
    public IEnumerable<DictionaryEntry> Transform(
        EnglishChineseRawEntry raw)
    {
        if (raw == null)
            throw new ArgumentNullException(nameof(raw));

        // Split at the dictionary separator
        var idx = raw.RawLine.IndexOf('⬄');
        if (idx < 0 || idx == raw.RawLine.Length - 1)
            yield break; // invalid entry, defensive

        // RHS only (definition side)
        var rhs = raw.RawLine.Substring(idx + 1).Trim();

        if (rhs.Length == 0)
            yield break;

        yield return new DictionaryEntry
        {
            Word = raw.Headword,
            NormalizedWord = Normalize(raw.Headword),
            Definition = rhs,
            SenseNumber = 1,
            SourceCode = "ENG_CHN",
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var s = input.ToLowerInvariant();

        // Remove parentheses
        s = s.Replace("(", "")
            .Replace(")", "");

        // Replace commas with space
        s = s.Replace(",", " ");

        // Collapse whitespace
        s = Regex.Replace(s, @"\s+", " ").Trim();

        return s;
    }
}