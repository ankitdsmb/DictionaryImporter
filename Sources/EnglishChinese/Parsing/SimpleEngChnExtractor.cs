using System;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.EnglishChinese
{
    public static class SimpleEngChnExtractor
    {
        public static string ExtractDefinition(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return string.Empty;

            var line = rawLine.Trim();

            // 1. Handle ⬄ separator
            if (line.Contains('⬄'))
            {
                var idx = line.IndexOf('⬄');
                var afterSeparator = line.Substring(idx + 1).Trim();
                return RemoveEtymology(afterSeparator);
            }

            // 2. Handle pattern with pronunciation: word [/pron/] pos. definition
            if (line.Contains('/') && line.Contains('.'))
            {
                try
                {
                    // Find the part after the last slash
                    var lastSlash = line.LastIndexOf('/');
                    if (lastSlash > 0)
                    {
                        var afterSlash = line.Substring(lastSlash + 1);
                        var periodIdx = afterSlash.IndexOf('.');
                        if (periodIdx > 0)
                        {
                            var afterPeriod = afterSlash.Substring(periodIdx + 1).Trim();

                            // Find where actual definition starts - look for Chinese content
                            for (int i = 0; i < afterPeriod.Length; i++)
                            {
                                if (ShouldStartExtractionAt(afterPeriod, i))
                                {
                                    // Include starting from index i
                                    var definition = afterPeriod.Substring(i);
                                    return RemoveEtymology(definition.Trim());
                                }
                            }

                            // If no clear start found, return everything after period
                            return RemoveEtymology(afterPeriod);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error if needed
                    Console.WriteLine($"Error extracting Chinese: {ex.Message}");
                }
            }

            // 3. Return original line as fallback
            return RemoveEtymology(line);
        }

        private static bool ShouldStartExtractionAt(string text, int index)
        {
            if (index >= text.Length) return false;

            var c = text[index];

            // Check if character is Chinese punctuation (including 〔)
            if (IsChinesePunctuationChar(c))
                return true;

            // Check if character is Chinese
            if (IsChineseChar(c))
                return true;

            // Check if it's a digit followed by Chinese content
            if (char.IsDigit(c))
            {
                // Look ahead to see if Chinese follows
                for (int j = index + 1; j < Math.Min(index + 3, text.Length); j++)
                {
                    if (IsChineseChar(text[j]) || IsChinesePunctuationChar(text[j]))
                        return true;
                }
            }

            return false;
        }

        // ✅ FIXED: Extract part of speech - handle abbreviations that also have POS markers
        public static string ExtractPartOfSpeech(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return null;

            var line = rawLine.Trim();

            // Extract the headword first
            var word = ExtractHeadword(line);

            // Check if word is an abbreviation (like "2, 4-D")
            bool isAbbreviation = IsAbbreviation(word);

            // Try to extract POS marker from the text
            string posFromMarker = ExtractPosFromMarker(line);

            // If word is an abbreviation AND we found a POS marker
            // For abbreviations, we should return "abbreviation" not the POS
            if (isAbbreviation)
            {
                return "abbreviation";
            }

            // If not an abbreviation, return the POS marker if found
            if (!string.IsNullOrWhiteSpace(posFromMarker))
            {
                return posFromMarker;
            }

            return null;
        }

        private static string ExtractPosFromMarker(string line)
        {
            // Look for POS pattern: space, letters (1-4 chars), period, space
            var match = Regex.Match(line, @"\s+(n|v|adj|adv|pron|prep|conj|interj|abbr|phr|pl|sing|a)\.\s", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                // Try alternative pattern without the trailing space
                match = Regex.Match(line, @"\s+(n|v|adj|adv|pron|prep|conj|interj|abbr|phr|pl|sing|a)\.(?:$|[^\w])", RegexOptions.IgnoreCase);
            }

            if (match.Success)
            {
                var posAbbr = match.Groups[1].Value.ToLowerInvariant();

                return posAbbr switch
                {
                    "n" => "noun",
                    "v" => "verb",
                    "a" or "adj" => "adj",
                    "adv" => "adv",
                    "pron" => "pronoun",
                    "prep" => "preposition",
                    "conj" => "conjunction",
                    "interj" => "interjection",
                    "abbr" => "abbreviation",
                    "phr" => "phrase",
                    "pl" => "plural",
                    "sing" => "singular",
                    _ => posAbbr
                };
            }

            return null;
        }

        private static string ExtractHeadword(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            // Find first space or special character
            var endIndex = line.IndexOf(' ');
            if (endIndex <= 0) endIndex = line.Length;

            var headword = line.Substring(0, endIndex).Trim();

            // Clean up any trailing punctuation
            headword = headword.TrimEnd('.', ',', ';', ':', '!', '?', '·');

            return headword;
        }

        private static bool IsAbbreviation(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            // Clean the word
            var cleanWord = word.Replace(",", "").Replace(".", "");

            // Check for TRUE abbreviation patterns:

            // 1. Contains digits AND capital letters (like "2,4-D", "3D", "A1")
            if (Regex.IsMatch(cleanWord, @"\d+[A-Z]|[A-Z]\d+"))
                return true;

            // 2. ALL UPPERCASE with possible digits and hyphens (like "USA", "DNA", "HIV-AIDS")
            if (cleanWord.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '-'))
                return true;

            // 3. Contains dots between letters (like "U.S.A.", "a.m.", "p.m.")
            if (word.Contains('.') && Regex.IsMatch(word, @"[A-Za-z]\.[A-Za-z]"))
                return true;

            // 4. Very short (1-3 chars) and looks like initials (mostly uppercase)
            if (word.Length <= 3)
            {
                var uppercaseCount = word.Count(char.IsUpper);
                var letterCount = word.Count(char.IsLetter);
                if (letterCount > 0 && (uppercaseCount == letterCount || word.Contains('.')))
                    return true;
            }

            // 5. Specific chemical/technical abbreviations (like "2,4-D")
            if (Regex.IsMatch(word, @"^\d+(?:,\s*\d+)*\-[A-Z]$"))
                return true;

            // NOT abbreviations:
            // - "18-wheel·er" - contains digits but also lowercase letters and middle dot
            // - "e-mail" - has hyphen but mixed case
            // - "can't" - has apostrophe but is a regular word

            return false;
        }

        private static string RemoveEtymology(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Remove [ < etymology ] markers
            var bracketIdx = text.IndexOf('[');
            if (bracketIdx > 0)
            {
                return text.Substring(0, bracketIdx).Trim();
            }

            // Also remove < etymology > markers
            var angleIdx = text.IndexOf('<');
            if (angleIdx > 0)
            {
                // Only remove if it looks like etymology (not part of Chinese text)
                var beforeAngle = text.Substring(0, angleIdx);
                if (ContainsChinese(beforeAngle))
                {
                    return beforeAngle.Trim();
                }
            }

            return text.Trim();
        }

        private static bool IsChinesePunctuationChar(char c)
        {
            // Common Chinese punctuation - INCLUDING 〔
            return c == '〔' || c == '〕' || c == '【' || c == '】' ||
                   c == '（' || c == '）' || c == '《' || c == '》' ||
                   c == '。' || c == '；' || c == '，' || c == '、' ||
                   c == '「' || c == '」' || c == '『' || c == '』' ||
                   c == '〖' || c == '〗' || c == '〈' || c == '〉';
        }

        private static bool IsChineseChar(char c)
        {
            // Simplified check for Chinese characters
            int code = (int)c;
            return (code >= 0x4E00 && code <= 0x9FFF) ||   // CJK Unified Ideographs
                   (code >= 0x3400 && code <= 0x4DBF);     // CJK Extension A
        }

        public static bool ContainsChinese(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (char c in text)
            {
                if (IsChineseChar(c) || IsChinesePunctuationChar(c))
                    return true;
            }

            return false;
        }
    }
}