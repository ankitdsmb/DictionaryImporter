namespace DictionaryImporter.Core.Text
{
    public static class DefinitionExtractor
    {
        public static string ExtractDefinitionFromFormattedText(string formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return string.Empty;

            // Check if this appears to be only POS markers without actual content
            if (IsPosOnlyContent(formattedText))
            {
                return ExtractFromPosOnlyContent(formattedText);
            }

            // Split by lines
            var lines = formattedText.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .ToList();

            // If only POS markers exist, return empty
            if (lines.All(line => line.StartsWith("【POS】")))
                return string.Empty;

            // Find the Sense line
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("【Sense") && lines[i].EndsWith("】"))
                {
                    // The definition is typically 1-2 lines after the Sense marker
                    if (i + 2 < lines.Count)
                    {
                        // Check if next line is a number (like "5)" in your example)
                        if (IsNumberedLine(lines[i + 1]))
                        {
                            return ExtractFromNumberedLine(lines[i + 1]);
                        }
                        else if (i + 2 < lines.Count && IsNumberedLine(lines[i + 2]))
                        {
                            return ExtractFromNumberedLine(lines[i + 2]);
                        }
                        else
                        {
                            // Fallback: take the first non-empty, non-marker line after Sense
                            for (int j = i + 1; j < lines.Count; j++)
                            {
                                if (!IsSectionMarker(lines[j]) && !IsNumberedLine(lines[j]) && !string.IsNullOrWhiteSpace(lines[j]))
                                {
                                    return lines[j];
                                }
                            }
                        }
                    }
                }
            }

            // If no Sense marker found, try to find the definition directly
            // Look for numbered lines that might contain definitions
            foreach (var line in lines)
            {
                if (IsNumberedLine(line))
                {
                    var definition = ExtractFromNumberedLine(line);
                    if (!string.IsNullOrWhiteSpace(definition) && definition.Length > 2)
                        return definition;
                }
            }

            // Check for any line that looks like a definition (not a marker)
            foreach (var line in lines)
            {
                if (!IsSectionMarker(line) && !IsPosMarker(line) && !string.IsNullOrWhiteSpace(line) && line.Length > 2)
                {
                    // Make sure it's not just a single character or very short text
                    if (line.Length > 3 || (line.Length == 3 && char.IsLetter(line[0])))
                        return line;
                }
            }

            // If we have content but couldn't extract a definition, check if it's valid
            if (lines.Count > 0)
            {
                // Check if it's structured content (has markers) but no definition
                bool hasContentMarkers = lines.Any(line =>
                    line.StartsWith("【POS】") ||
                    line.StartsWith("【Pronunciation】") ||
                    line.StartsWith("【Etymology】") ||
                    line.StartsWith("【Sense"));

                if (hasContentMarkers)
                {
                    // This appears to be structured content without a definition
                    return string.Empty;
                }

                // Return the first line that's not a marker as fallback
                var firstNonMarker = lines.FirstOrDefault(line =>
                    !IsSectionMarker(line) && !IsPosMarker(line));

                if (!string.IsNullOrWhiteSpace(firstNonMarker))
                    return firstNonMarker;
            }

            return string.Empty;
        }

        private static bool IsPosOnlyContent(string text)
        {
            // Check if the content consists mostly of POS markers
            var lines = text.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .ToList();

            if (lines.Count == 0)
                return false;

            // Count how many lines are POS markers
            int posMarkerCount = lines.Count(line => line.StartsWith("【POS】"));

            // If more than 50% of lines are POS markers and no actual content
            return posMarkerCount > 0 &&
                   (posMarkerCount >= lines.Count / 2 ||
                   lines.All(line => line.StartsWith("【POS】") || IsSectionMarker(line)));
        }

        private static string ExtractFromPosOnlyContent(string text)
        {
            // For POS-only content, we can try to extract from the context
            // Look for any non-POS content
            var lines = text.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith("【POS】"))
                .ToList();

            foreach (var line in lines)
            {
                if (!IsSectionMarker(line) && line.Length > 2)
                    return line;
            }

            return string.Empty;
        }

        private static bool IsSectionMarker(string line)
        {
            return line.StartsWith("【") && line.EndsWith("】");
        }

        private static bool IsPosMarker(string line)
        {
            return line.StartsWith("【POS】");
        }

        private static bool IsNumberedLine(string line)
        {
            return Regex.IsMatch(line, @"^\d+\)\s");
        }

        private static string ExtractFromNumberedLine(string line)
        {
            var match = Regex.Match(line, @"^\d+\)\s*(.+)");
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return line;
        }
    }
}