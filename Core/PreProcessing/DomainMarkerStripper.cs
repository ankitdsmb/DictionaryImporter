using System.Text.RegularExpressions;

internal static class DomainMarkerStripper
{
    private static readonly Regex Marker =
        new(@"^[\(\[【].+?[\)\]】]\s*", RegexOptions.Compiled);

    public static string Strip(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        return Marker.Replace(word, "").Trim();
    }
}