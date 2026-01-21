using System.Text.RegularExpressions;
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
                return raw;

            raw = ocr.Normalize(raw);
            raw = definitionNormalizer.Normalize(raw);

            raw = NormalizePunctuation(raw);

            // ✅ “Modern feel”: use bullets instead of 1) 2) if multi-line
            if (_opt.Style.Equals("Modern", StringComparison.OrdinalIgnoreCase))
            {
                if (_opt.UseBulletsForMultiLineDefinitions && raw.Contains('\n'))
                {
                    var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .ToList();

                    // remove existing "1) " if already numbered
                    for (int i = 0; i < lines.Count; i++)
                        lines[i] = Regex.Replace(lines[i], @"^\d+\)\s*", "");

                    raw = string.Join("\n", lines.Select(x => $"• {x}"));
                }
            }

            return raw.Trim();
        }

        public string FormatExample(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            raw = ocr.Normalize(raw);
            raw = NormalizePunctuation(raw);

            // ✅ Modern: wrap examples with quotes
            if (_opt.Style.Equals("Modern", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Trim();

                // avoid double quotes if already wrapped
                if (!raw.StartsWith("\"") && !raw.StartsWith("'"))
                    raw = $"“{raw}”";
            }

            return raw.Trim();
        }

        public string? FormatSynonym(string raw)
        {
            return FormatSingleWordTerm(raw);
        }

        public string? FormatAntonym(string raw)
        {
            return FormatSingleWordTerm(raw);
        }

        public string FormatEtymology(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            raw = ocr.Normalize(raw);

            // Etymology should remain close to source
            raw = Regex.Replace(raw, @"\s+", " ").Trim();

            return raw;
        }

        private string? FormatSingleWordTerm(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();

            // remove trailing punctuation
            raw = raw.TrimEnd('.', ',', ';', ':', '!', '?');

            // normalize spaces
            raw = Regex.Replace(raw, @"\s+", " ").Trim();

            // reject too short
            if (raw.Length < 2)
                return null;

            // reject full sentences
            if (raw.Contains('.') || raw.Length > 40)
                return null;

            return raw;
        }

        private string NormalizePunctuation(string text)
        {
            text = text.Replace("“", "\"").Replace("”", "\"").Replace("’", "'");

            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");
            text = Regex.Replace(text, @"([,.;:!?])([A-Za-z])", "$1 $2");

            text = Regex.Replace(text, @"([(\[])\s+", "$1");
            text = Regex.Replace(text, @"\s+([)\]])", "$1");

            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (!_opt.KeepSemicolons)
                text = text.Replace(";", ",");

            return text;
        }

        public string FormatNote(string note)
        {
            throw new NotImplementedException();
        }

        public string FormatDomain(string domain)
        {
            throw new NotImplementedException();
        }

        public string FormatUsageLabel(string usageLabel)
        {
            throw new NotImplementedException();
        }

        public string FormatCrossReference(CrossReference crossReference)
        {
            throw new NotImplementedException();
        }

        public string CleanHtml(string html)
        {
            throw new NotImplementedException();
        }

        public string NormalizeSpacing(string text)
        {
            throw new NotImplementedException();
        }

        public string EnsureProperPunctuation(string text)
        {
            throw new NotImplementedException();
        }

        public string RemoveFormattingMarkers(string text)
        {
            throw new NotImplementedException();
        }
    }
}