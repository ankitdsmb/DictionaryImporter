using System.Text;

namespace DictionaryImporter.Core.PreProcessing;

/// <summary>
///     Normalizes IPA strings for stable storage and comparison.
///     Does NOT change phonetic meaning.
/// </summary>
internal static class IpaNormalizer
{
    public static string Normalize(string ipa)
    {
        if (string.IsNullOrWhiteSpace(ipa))
            return ipa;

        // 1. Unicode normalization
        ipa = ipa.Normalize(NormalizationForm.FormC);

        var sb = new StringBuilder(ipa.Length);

        foreach (var ch in ipa)
            switch (ch)
            {
                // Normalize length
                case ':':
                    sb.Append('ː');
                    break;

                // Normalize tie bars
                case '͡': // combining double inverted breve
                    sb.Append('͜');
                    break;

                // Remove editorial separators
                case '.':
                case ',':
                case '，':
                    // skip
                    break;

                // Normalize whitespace
                case ' ':
                case '\t':
                case '\n':
                    sb.Append(' ');
                    break;

                default:
                    sb.Append(ch);
                    break;
            }

        // Collapse whitespace
        return sb
            .ToString()
            .Trim()
            .Replace("  ", " ");
    }
}