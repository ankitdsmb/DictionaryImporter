namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterSenseRegex
    {
        public static readonly Regex NumberedSense =
            new(
                @"(?<!\w)(?<num>\d+)\.\s+(?<body>[^0-9]+)",
                RegexOptions.Compiled);

        public static readonly Regex Lettered =
            new(
                @"\((?<letter>[a-z])\)\s+(?<body>[^()]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}