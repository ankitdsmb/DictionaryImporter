using System.Globalization;

namespace DictionaryImporter.Infrastructure.FragmentStore;

public sealed class WordNormalizer(bool caseSensitive)
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public string Normalize(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return "_";

        return _cache.GetOrAdd(word, NormalizeCore);
    }

    private string NormalizeCore(string word)
    {
        Span<char> buffer = stackalloc char[word.Length];
        int index = 0;

        foreach (var c in word.Normalize(NormalizationForm.FormD))
        {
            if (Char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            var ch = caseSensitive ? c : char.ToLowerInvariant(c);

            if (char.IsLetterOrDigit(ch) || ch == '_')
                buffer[index++] = ch;
        }

        return index == 0 ? "_" : new string(buffer[..index]);
    }
}