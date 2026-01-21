using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Sources.Common
{
    public class EnglishChineseParsingHelper
    {
        public static string ExtractChineseDefinition(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Handle ⬄ separator
            if (text.Contains('⬄'))
            {
                var parts = text.Split('⬄', 2);
                if (parts.Length > 1)
                {
                    var result = parts[1].Trim();
                    // Remove etymology markers if present
                    return RemoveEtymologyMarkers(result);
                }
            }

            // Handle pattern: word [/pronunciation/] n. chinese definition
            if (text.Contains('/') && text.Contains('.'))
            {
                try
                {
                    // Find the part after the last slash
                    var lastSlash = text.LastIndexOf('/');
                    if (lastSlash > 0)
                    {
                        // Find the period after the slash
                        var periodIdx = text.IndexOf('.', lastSlash);
                        if (periodIdx > lastSlash)
                        {
                            var afterPeriod = text.Substring(periodIdx + 1).Trim();

                            // Find where Chinese content starts (including digits at start)
                            for (int i = 0; i < afterPeriod.Length; i++)
                            {
                                if (ShouldStartExtractionAt(afterPeriod, i))
                                {
                                    var extracted = afterPeriod.Substring(i);
                                    // Remove etymology markers
                                    return RemoveEtymologyMarkers(extracted.Trim());
                                }
                            }

                            // If no clear start found, use everything after period
                            return RemoveEtymologyMarkers(afterPeriod);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error if logger available
                    Console.WriteLine($"Error extracting Chinese: {ex.Message}");
                }
            }

            // Fallback: return trimmed text
            return RemoveEtymologyMarkers(text.Trim());
        }

        private static bool ShouldStartExtractionAt(string text, int index)
        {
            if (index >= text.Length) return false;

            var c = text[index];

            // Always include digits at the start of Chinese content
            if (char.IsDigit(c))
            {
                // Check if it's part of Chinese context
                // Look ahead to see if there are Chinese characters nearby
                for (int i = index + 1; i < Math.Min(index + 5, text.Length); i++)
                {
                    if (IsChineseCharacter(text[i]) || IsChinesePunctuation(text[i]))
                    {
                        return true;
                    }
                }
            }

            // Check for Chinese punctuation marks
            if (IsChinesePunctuation(c))
                return true;

            // Check for Chinese characters
            if (IsChineseCharacter(c))
                return true;

            return false;
        }

        private static bool IsChinesePunctuation(char c)
        {
            // Chinese punctuation marks
            var chinesePunctuation = new HashSet<char>
        {
            '〔', '〕', '【', '】', '（', '）', '《', '》',
            '「', '」', '『', '』', '〖', '〗', '〈', '〉',
            '。', '；', '，', '、', '・', '…', '‥', '—',
            '～', '・', '‧', '﹑', '﹒', '﹔', '﹕', '﹖',
            '﹗', '﹘', '﹙', '﹚', '﹛', '﹜', '﹝', '﹞',
            '﹟', '﹠', '﹡', '﹢', '﹣', '﹤', '﹥', '﹦',
            '﹨', '﹩', '﹪', '﹫'
        };

            return chinesePunctuation.Contains(c);
        }

        private static bool IsChineseCharacter(char c)
        {
            int code = (int)c;
            return (code >= 0x4E00 && code <= 0x9FFF) ||   // CJK Unified Ideographs
                   (code >= 0x3400 && code <= 0x4DBF) ||   // CJK Extension A
                   (code >= 0x20000 && code <= 0x2A6DF) || // CJK Extension B
                   (code >= 0x2A700 && code <= 0x2B73F) || // CJK Extension C
                   (code >= 0x2B740 && code <= 0x2B81F) || // CJK Extension D
                   (code >= 0x2B820 && code <= 0x2CEAF);   // CJK Extension E
        }

        private static string RemoveEtymologyMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Remove etymology in brackets: [ < ... ]
            var bracketIdx = text.IndexOf('[');
            if (bracketIdx > 0)
            {
                return text.Substring(0, bracketIdx).Trim();
            }

            // Remove etymology with angle brackets: < ... >
            var angleIdx = text.IndexOf('<');
            if (angleIdx > 0)
            {
                // Check if it's likely etymology (has closing >)
                var closeAngleIdx = text.IndexOf('>', angleIdx);
                if (closeAngleIdx > angleIdx)
                {
                    // Check if there's text before the < that looks like Chinese
                    var beforeAngle = text.Substring(0, angleIdx).Trim();
                    if (ContainsChinese(beforeAngle))
                    {
                        return beforeAngle;
                    }
                }
            }

            return text.Trim();
        }

        private static bool ContainsChinese(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (char c in text)
            {
                if (IsChineseCharacter(c) || IsChinesePunctuation(c))
                    return true;
            }

            return false;
        }
    }
}