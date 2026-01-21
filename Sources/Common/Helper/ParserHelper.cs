namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Provides helper methods for dictionary parsers.
    /// </summary>
    public static class ParserHelper
    {
        /// <summary>
        /// Validates if a meaning title is valid.
        /// </summary>
        public static bool IsValidMeaningTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            var trimmed = title.Trim();

            if (trimmed.StartsWith("[") || trimmed.StartsWith("("))
                return false;

            return char.IsLetter(trimmed[0]);
        }

        /// <summary>
        /// Extracts the main definition from text containing markers.
        /// </summary>
        public static string? ExtractMainDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var firstMarkerIndex = definition.IndexOf("【");
            if (firstMarkerIndex >= 0)
                return definition.Substring(0, firstMarkerIndex).Trim();

            return definition.Trim();
        }

        /// <summary>
        /// Extracts a section from definition text marked with specific markers.
        /// </summary>
        public static string? ExtractSection(string definition, string marker)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            var startIndex = definition.IndexOf(marker);
            if (startIndex < 0)
                return null;

            startIndex += marker.Length;
            var endIndex = definition.IndexOf("【", startIndex);

            if (endIndex < 0)
                endIndex = definition.Length;

            return definition.Substring(startIndex, endIndex - startIndex).Trim();
        }
    }
}