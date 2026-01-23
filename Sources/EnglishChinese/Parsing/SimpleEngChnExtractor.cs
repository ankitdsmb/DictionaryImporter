namespace DictionaryImporter.Sources.EnglishChinese.Parsing
{
    public static class SimpleEngChnExtractor
    {
        public static string ExtractDefinition(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return string.Empty;
            var line = rawLine.Trim();

            // 1. Handle ⬄ separator - extract everything AFTER the separator
            if (line.Contains('⬄'))
            {
                var idx = line.IndexOf('⬄');
                var afterSeparator = line.Substring(idx + 1).Trim();

                // FIX: Return ALL content after ⬄, don't aggressively clean Chinese
                // Only clean basic formatting, preserve Chinese characters
                return CleanChineseDefinitionPreservingContent(afterSeparator);
            }

            // 2. Handle lines without separator but with pronunciation
            if (line.Contains('/') && line.Contains('.'))
            {
                try
                {
                    // Find the part after the last slash (pronunciation ends with /)
                    var lastSlash = line.LastIndexOf('/');
                    if (lastSlash > 0)
                    {
                        var afterSlash = line.Substring(lastSlash + 1);
                        var periodIdx = afterSlash.IndexOf('.');
                        if (periodIdx > 0)
                        {
                            var afterPeriod = afterSlash.Substring(periodIdx + 1).Trim();
                            return CleanChineseDefinitionPreservingContent(afterPeriod);
                        }
                    }
                }
                catch
                {
                    // Fall through
                }
            }

            // 3. Return original line as fallback (with basic cleaning)
            return CleanChineseDefinitionPreservingContent(line);
        }

        public static string ExtractPartOfSpeech(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return null;
            var line = rawLine.Trim();

            // 1. Extract headword
            var headword = ExtractHeadword(line);
            if (string.IsNullOrWhiteSpace(headword)) return ExtractPosFromMarker(line);

            // 2. Check for chemical abbreviation pattern: "2, 4-D", "2,4-D", etc.
            // This must happen BEFORE checking POS marker
            // First check the exact pattern with optional space: digit, comma, optional spaces, digit, hyphen, letter
            if (Regex.IsMatch(headword, @"^\d+,\s*\d+\-[A-Za-z]$"))
            {
                return "abbreviation";
            }

            // Also check without spaces
            var cleanHeadword = headword.Replace(" ", "");
            if (Regex.IsMatch(cleanHeadword, @"^\d+,\d+\-[A-Za-z]$"))
            {
                return "abbreviation";
            }

            // 3. Check other abbreviation patterns
            if (IsAbbreviation(headword))
            {
                return "abbreviation";
            }

            // 4. ONLY if not an abbreviation, look for POS marker
            return ExtractPosFromMarker(line);
        }

        private static string ExtractPosFromMarker(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            // Look for POS pattern: space, letters (1-4 chars), period
            var match = Regex.Match(line, @"\s+(n|v|adj|adv|pron|prep|conj|interj|abbr|phr|pl|sing|a)\.\s*", RegexOptions.IgnoreCase);
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

        // FIX: This method was too aggressive - preserve ALL Chinese content
        private static string CleanChineseDefinitionPreservingContent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var cleaned = text;

            // Remove etymology markers [ < ... ] but preserve everything else
            // Only remove if the content before bracket is meaningful
            var bracketIdx = cleaned.IndexOf('[');
            if (bracketIdx > 0)
            {
                var beforeBracket = cleaned.Substring(0, bracketIdx).Trim();
                // Only truncate if there's meaningful content before bracket
                if (beforeBracket.Length > 3)
                {
                    cleaned = beforeBracket;
                }
            }

            // Remove etymology with angle brackets
            var angleIdx = cleaned.IndexOf('<');
            if (angleIdx > 0)
            {
                var beforeAngle = cleaned.Substring(0, angleIdx).Trim();
                if (beforeAngle.Length > 3)
                {
                    cleaned = beforeAngle;
                }
            }

            // Normalize whitespace but preserve ALL content
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        public static bool ContainsChinese(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (char c in text)
            {
                if (IsChineseCharacter(c) || IsChinesePunctuationChar(c)) return true;
            }
            return false;
        }

        private static bool IsChineseCharacter(char c)
        {
            int code = (int)c;
            return (code >= 0x4E00 && code <= 0x9FFF) || // CJK Unified Ideographs
                   (code >= 0x3400 && code <= 0x4DBF);   // CJK Extension A
        }

        private static bool IsChinesePunctuationChar(char c)
        {
            return c == '〔' || c == '〕' || c == '【' || c == '】' || c == '（' || c == '）' ||
                   c == '《' || c == '》' || c == '。' || c == '；' || c == '，' || c == '、' ||
                   c == '「' || c == '」' || c == '『' || c == '』' || c == '〖' || c == '〗' ||
                   c == '〈' || c == '〉';
        }

        private static string ExtractHeadword(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;

            // Find content before ⬄ separator
            if (line.Contains('⬄'))
            {
                var idx = line.IndexOf('⬄');
                var beforeSeparator = line.Substring(0, idx).Trim();
                return beforeSeparator;
            }

            // For "2, 4-D /ˌtuːˌfɔːˈdiː/ n. ...", we need to extract "2, 4-D"
            // Pattern: word [pronunciation] pos. definition
            // Extract until space-slash or space-bracket
            var endIndex = line.IndexOfAny(new[] { '/', '[' });
            if (endIndex > 0)
            {
                return line.Substring(0, endIndex).Trim();
            }

            // If no slash or bracket, extract first word (but for "2, 4-D" we need the whole thing)
            // Try regex to capture everything up to space-slash or end of string
            var match = Regex.Match(line, @"^(.+?)(?=\s*[\/\[]|$)");
            if (match.Success)
            {
                var result = match.Groups[1].Value.Trim();
                // For abbreviations like "2, 4-D", we might have extracted too much
                // Check if it ends with a pattern that looks like it could be part of the definition
                if (result.Contains('.') && result.Length > 10)
                {
                    // Might have included part of definition, backtrack to last space
                    var lastSpace = result.LastIndexOf(' ');
                    if (lastSpace > 0)
                    {
                        result = result.Substring(0, lastSpace).Trim();
                    }
                }
                return result;
            }

            // Fallback: extract until first space
            var firstSpace = line.IndexOf(' ');
            if (firstSpace > 0)
            {
                return line.Substring(0, firstSpace).Trim();
            }

            return line.Trim();
        }

        private static bool IsAbbreviation(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return false;

            // Handle "2, 4-D" pattern specifically
            if (Regex.IsMatch(word, @"^\d+,\s*\d+\-[A-Za-z]$")) return true;

            // Also check without spaces
            var cleanWord = word.Replace(" ", "");
            if (Regex.IsMatch(cleanWord, @"^\d+,\d+\-[A-Za-z]$")) return true;

            // Pattern 2: "3D", "4G" type
            if (Regex.IsMatch(cleanWord, @"^\d+[A-Za-z]$")) return true;

            // Pattern 3: All uppercase with possible digits, hyphens, commas
            // Check if mostly uppercase letters
            var letters = word.Where(char.IsLetter).ToList();
            if (letters.Count > 0)
            {
                var uppercaseRatio = letters.Count(c => char.IsUpper(c)) / (double)letters.Count;
                if (uppercaseRatio > 0.7 && word.Length <= 8) return true;
            }

            // Pattern 4: Contains dots (U.S.A., a.m., etc.) and is short
            if (word.Contains('.') && word.Length <= 6) return true;

            // Pattern 5: Very short (1-3 chars) and mostly uppercase
            if (word.Length <= 3)
            {
                var letterCount = word.Count(char.IsLetter);
                var uppercaseCount = word.Count(char.IsUpper);
                if (letterCount > 0 && uppercaseCount >= letterCount) return true;
            }

            return false;
        }
    }
}