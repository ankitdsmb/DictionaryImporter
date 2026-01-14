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

        ipa = ipa.Normalize(NormalizationForm.FormC);

        var sb = new StringBuilder(ipa.Length);

        foreach (var ch in ipa)
            switch (ch)
            {
                case ':':
                    sb.Append('ː');
                    break;

                case '͡':
                    sb.Append('͜');
                    break;

                case '.':
                case ',':
                case '，':
                    break;

                case ' ':
                case '\t':
                case '\n':
                    sb.Append(' ');
                    break;

                default:
                    sb.Append(ch);
                    break;
            }

        return sb
            .ToString()
            .Trim()
            .Replace("  ", " ");
    }
}