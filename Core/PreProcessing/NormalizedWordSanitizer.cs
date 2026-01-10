using System.Text.RegularExpressions;

internal static class NormalizedWordSanitizer
{
    private static readonly Regex Noise =
        new(@"[^\p{L}\s]", RegexOptions.Compiled);

    public static string Sanitize(string input, string language)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        if (language == "zh")
            return input.Trim();   // Chinese preserved verbatim

        var text = input.ToLowerInvariant();
        text = Noise.Replace(text, " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }
}