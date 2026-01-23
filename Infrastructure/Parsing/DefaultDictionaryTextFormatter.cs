// File: Infrastructure/Parsing/DefaultDictionaryTextFormatter.cs

using DictionaryImporter.Core.Text;

namespace DictionaryImporter.Infrastructure.Parsing
{
    public class DefaultDictionaryTextFormatter(ILogger<DefaultDictionaryTextFormatter> logger)
        : IDictionaryTextFormatter
    {
        private readonly ILogger<DefaultDictionaryTextFormatter> logger = logger;

        public string FormatDefinition(string definition)
        {
            return definition?.Trim() ?? string.Empty;
        }

        public string FormatExample(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            var formatted = example.Trim();
            if (!formatted.EndsWith(".") && !formatted.EndsWith("!") && !formatted.EndsWith("?"))
                formatted += ".";

            return formatted;
        }

        public string FormatSynonym(string synonym)
        {
            return synonym?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        // NEW: Implement missing methods
        public string FormatAntonym(string antonym)
        {
            return antonym?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        public string FormatEtymology(string etymology)
        {
            return etymology?.Trim() ?? string.Empty;
        }

        public string FormatNote(string note)
        {
            return note?.Trim() ?? string.Empty;
        }

        public string FormatDomain(string domain)
        {
            return domain?.Trim() ?? string.Empty;
        }

        public string FormatUsageLabel(string usageLabel)
        {
            return usageLabel?.Trim() ?? string.Empty;
        }

        public string FormatCrossReference(CrossReference crossReference)
        {
            if (crossReference == null)
                return string.Empty;

            return $"{crossReference.TargetWord} ({crossReference.ReferenceType})";
        }

        public string CleanHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            // Simple HTML cleaning - remove tags
            var cleaned = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(cleaned).Trim();
        }

        public string NormalizeSpacing(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Replace multiple spaces with single space
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }

        public string EnsureProperPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var trimmed = text.Trim();

            // Ensure it ends with proper punctuation
            if (!trimmed.EndsWith(".") && !trimmed.EndsWith("!") && !trimmed.EndsWith("?") &&
                !trimmed.EndsWith(":") && !trimmed.EndsWith(";") && !trimmed.EndsWith(","))
            {
                // If it's a complete sentence (starts with capital, has multiple words), add period
                if (trimmed.Length > 0 && char.IsUpper(trimmed[0]) && trimmed.Contains(" "))
                {
                    trimmed += ".";
                }
            }

            return trimmed;
        }

        public string RemoveFormattingMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove common formatting markers
            var markers = new[] { "★", "☆", "●", "○", "▶", "【", "】", "〖", "〗", "《", "》", "〈", "〉" };
            var result = text;

            foreach (var marker in markers)
            {
                result = result.Replace(marker, string.Empty);
            }

            return result.Trim();
        }
    }

}