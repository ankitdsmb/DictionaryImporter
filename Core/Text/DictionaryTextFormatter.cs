using System.Text.RegularExpressions;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.Core.Text
{
    public sealed class DictionaryTextFormatter(
        IOcrArtifactNormalizer ocr,
        IDefinitionNormalizer definitionNormalizer,
        IOptions<DictionaryTextFormattingOptions> options,
        ILogger<DictionaryTextFormatter> logger)
        : IDictionaryTextFormatter
    {
        private readonly DictionaryTextFormattingOptions _opt = options.Value;

        public string FormatDefinition(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            // 1. Basic OCR cleanup
            raw = ocr.Normalize(raw);

            // 2. Structural Normalization (merging lines, numbering)
            raw = definitionNormalizer.Normalize(raw);

            // 3. Punctuation cleanup
            raw = NormalizePunctuation(raw);

            // 4. Style application
            if (_opt.Style.Equals("Modern", StringComparison.OrdinalIgnoreCase))
            {
                if (_opt.UseBulletsForMultiLineDefinitions && raw.Contains('\n'))
                {
                    var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .ToList();

                    // Strip existing numbering like "1) " or "1." to apply uniform bullets
                    for (int i = 0; i < lines.Count; i++)
                        lines[i] = Regex.Replace(lines[i], @"^\d+[\)\.]\s*", "");

                    raw = string.Join("\n", lines.Select(x => $"• {x}"));
                }
            }

            return raw.Trim();
        }

        public string FormatExample(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            raw = ocr.Normalize(raw);
            raw = NormalizePunctuation(raw);
            raw = raw.Trim();

            // Ensure proper sentence ending for examples
            raw = EnsureProperPunctuation(raw);

            if (_opt.Style.Equals("Modern", StringComparison.OrdinalIgnoreCase))
            {
                // Smart quote wrapping
                if (!raw.StartsWith("\"") && !raw.StartsWith("'") && !raw.StartsWith("“"))
                    raw = $"“{raw}”";
            }

            return raw;
        }

        // Changed return type to string to match interface (assuming non-nullable based on usage)
        // If interface expects string?, change back to string?
        public string FormatSynonym(string raw)
        {
            var result = FormatSingleWordTerm(raw);
            return result ?? string.Empty;
        }

        public string FormatAntonym(string raw)
        {
            var result = FormatSingleWordTerm(raw);
            return result ?? string.Empty;
        }

        public string FormatEtymology(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            raw = ocr.Normalize(raw);
            // Collapse whitespace
            raw = Regex.Replace(raw, @"\s+", " ").Trim();
            // Remove brackets if they wrap the whole etymology [ ... ]
            if (raw.StartsWith("[") && raw.EndsWith("]"))
            {
                raw = raw.Substring(1, raw.Length - 2).Trim();
            }

            return raw;
        }

        public string FormatNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return string.Empty;

            var cleaned = ocr.Normalize(note);
            cleaned = NormalizePunctuation(cleaned);
            return EnsureProperPunctuation(cleaned);
        }

        public string FormatDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return string.Empty;

            // Domains usually don't need heavy OCR normalization, just whitespace
            var cleaned = domain.Trim().TrimEnd('.');
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLower());
        }

        public string FormatUsageLabel(string usageLabel)
        {
            if (string.IsNullOrWhiteSpace(usageLabel))
                return string.Empty;

            return usageLabel.Trim().ToLowerInvariant();
        }

        public string FormatCrossReference(CrossReference crossReference)
        {
            if (crossReference == null)
                return string.Empty;

            // Example output: "See also: Word (Synonym)"
            var typeLabel = !string.IsNullOrEmpty(crossReference.ReferenceType)
                ? $" ({crossReference.ReferenceType})"
                : "";

            return $"{crossReference.TargetWord}{typeLabel}";
        }

        public string CleanHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            // 1. Decode HTML entities (&amp; -> &)
            var text = WebUtility.HtmlDecode(html);

            // 2. Strip tags
            text = Regex.Replace(text, "<.*?>", string.Empty);

            return text.Trim();
        }

        public string NormalizeSpacing(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        public string EnsureProperPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var t = text.Trim();
            if (char.IsLetterOrDigit(t[^1]))
            {
                // If it ends in a letter/digit, add a period
                return t + ".";
            }
            return t;
        }

        public string RemoveFormattingMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Common dictionary markers like † (obsolete), * (hypothetical), etc.
            var markers = new[] { "†", "‡", "*", "¶", "§", "“", "”", "【", "】" };
            foreach (var m in markers)
            {
                text = text.Replace(m, "");
            }
            return NormalizeSpacing(text);
        }

        // --- Helpers ---

        private string? FormatSingleWordTerm(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();
            // Remove common trailing punctuation for single words
            raw = raw.TrimEnd('.', ',', ';', ':', '!', '?');
            raw = Regex.Replace(raw, @"\s+", " ").Trim();

            // Reject if too short or looks like a sentence
            if (raw.Length < 2 || raw.Length > 40 || raw.Contains('.'))
                return null;

            return raw.ToLowerInvariant();
        }

        private string NormalizePunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Fix quotes
            text = text.Replace("“", "\"").Replace("”", "\"").Replace("’", "'");

            // Fix space before punctuation: "word ." -> "word."
            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");

            // Fix missing space after punctuation: "word.Next" -> "word. Next"
            // Be careful not to break acronyms or numbers (e.g., 1.2 or U.S.A.)
            // Logic: Punctuation followed immediately by a capital letter
            text = Regex.Replace(text, @"([.;:!?])([A-Z])", "$1 $2");

            // Fix bracket spacing: "word ( note )" -> "word (note)"
            text = Regex.Replace(text, @"\(\s+", "(");
            text = Regex.Replace(text, @"\s+\)", ")");

            // Standardize spaces
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (!_opt.KeepSemicolons)
                text = text.Replace(";", ",");

            return text;
        }
    }
}