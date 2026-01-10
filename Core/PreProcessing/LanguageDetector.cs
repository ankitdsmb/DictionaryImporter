internal static class LanguageDetector
{
    public static string Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en";

        foreach (var c in text)
        {
            if (c >= '\u4E00' && c <= '\u9FFF')
                return "zh";
        }

        return "en";
    }
}