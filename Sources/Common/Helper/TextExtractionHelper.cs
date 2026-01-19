using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Provides shared text extraction utilities for dictionary sources.
    /// </summary>
    public static class TextExtractionHelper
    {
        #region Common Regex Patterns

        private static readonly Regex HasEnglishLetter = new("[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex IpaRegex = new(@"/[^/]+/", RegexOptions.Compiled);
        private static readonly Regex EnglishSyllableRegex = new(@"^\s*[A-Za-z]+(?:·[A-Za-z]+)+\s*", RegexOptions.Compiled);

        private static readonly Regex PosRegex = new(@"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PosOnlyRegex = new(@"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion Common Regex Patterns

        #region Headword Detection Methods

        /// <summary>
        /// Determines if a line contains a dictionary headword.
        /// </summary>
        public static bool IsHeadword(string line, int maxLength = 40, bool requireUppercase = true)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var text = line.Trim();

            // Check length limit
            if (text.Length > maxLength)
                return false;

            // Check uppercase requirement (for Gutenberg-style dictionaries)
            if (requireUppercase && !text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal))
                return false;

            // Must contain at least one letter
            if (!text.Any(char.IsLetter))
                return false;

            return true;
        }

        /// <summary>
        /// Checks if text contains English letters.
        /// </summary>
        public static bool ContainsEnglishLetters(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && HasEnglishLetter.IsMatch(text);
        }

        /// <summary>
        /// Extracts headword from a line using a separator character.
        /// </summary>
        public static string? ExtractHeadwordFromSeparator(string line, char separator, int maxLength = 200)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var sepIndex = line.IndexOf(separator);
            if (sepIndex <= 0)
                return null;

            var headword = line.Substring(0, sepIndex).Trim();

            if (!ContainsEnglishLetters(headword))
                return null;

            if (headword.Length > maxLength)
                return null;

            return headword;
        }

        #endregion Headword Detection Methods

        #region Text Processing Methods

        /// <summary>
        /// Removes IPA pronunciation markers from text.
        /// </summary>
        public static string RemoveIpaMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return IpaRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// Removes English syllable markers from text.
        /// </summary>
        public static string RemoveSyllableMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return EnglishSyllableRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// Removes part-of-speech markers from the beginning of text.
        /// </summary>
        public static string RemovePosMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return PosRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// Checks if text contains only a part-of-speech marker.
        /// </summary>
        public static bool IsPosOnly(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && PosOnlyRegex.IsMatch(text);
        }

        /// <summary>
        /// Removes the headword from the beginning of definition text.
        /// </summary>
        public static string RemoveHeadwordFromDefinition(string definition, string headword)
        {
            if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(headword))
                return definition ?? string.Empty;

            var escapedHeadword = Regex.Escape(headword);
            return Regex.Replace(
                definition,
                @"^\s*" + escapedHeadword + @"\s+",
                string.Empty,
                RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Removes separator characters from text.
        /// </summary>
        public static string RemoveSeparators(string text, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = text;
            foreach (var separator in separators)
            {
                result = result.Replace(separator.ToString(), "");
            }

            return result;
        }

        /// <summary>
        /// Normalizes whitespace in text.
        /// </summary>
        public static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        #endregion Text Processing Methods

        #region Stream Processing Methods

        /// <summary>
        /// Creates a stream reader with optimized settings for dictionary files.
        /// </summary>
        public static StreamReader CreateDictionaryStreamReader(Stream stream)
        {
            return new StreamReader(
                stream,
                Encoding.UTF8,
                false,
                16 * 1024, // 16KB buffer
                true);     // Leave stream open
        }

        /// <summary>
        /// Processes a stream line by line with Gutenberg-style start/end markers.
        /// </summary>
        public static async IAsyncEnumerable<string> ProcessGutenbergStreamAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var reader = CreateDictionaryStreamReader(stream);
            string? line;
            var bodyStarted = false;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!bodyStarted)
                {
                    if (line.StartsWith("*** START"))
                    {
                        bodyStarted = true;
                        continue;
                    }
                    continue;
                }

                if (line.StartsWith("*** END"))
                    break;

                yield return line;
            }
        }

        /// <summary>
        /// Processes a stream line by line with progress tracking.
        /// </summary>
        public static async IAsyncEnumerable<string> ProcessStreamWithProgressAsync(
            Stream stream,
            ILogger logger,
            string sourceName,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream);
            long lineCount = 0;
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineCount++;

                if (lineCount % 10000 == 0)
                {
                    logger.LogInformation("{Source} processing progress: {Count} lines processed",
                        sourceName, lineCount);
                }

                yield return line;
            }

            logger.LogInformation("{Source} processing completed: {Count} total lines",
                sourceName, lineCount);
        }

        #endregion Stream Processing Methods
    }
}