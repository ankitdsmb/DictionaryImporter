namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseExtractor(ILogger<EnglishChineseExtractor> logger)
    : IDataExtractor<EnglishChineseRawEntry>
{
    private const string SourceCode = "ENG_CHN";
    private readonly ILogger<EnglishChineseExtractor> _logger = logger;

    // This is the CORRECT signature for IDataExtractor<T>
    public async IAsyncEnumerable<EnglishChineseRawEntry> ExtractAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);

        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // SIMPLIFIED: Extract headword - take everything before first space or ⬄
            var headword = ExtractSimpleHeadword(trimmedLine);
            headword = string.IsNullOrEmpty(headword) ? ExtractHeadwordFromEngChnLine(trimmedLine) : headword;
            if (headword == null)
                continue;

            var entry = new EnglishChineseRawEntry
            {
                Headword = headword,
                RawLine = trimmedLine
            };

            // Basic validation
            if (string.IsNullOrWhiteSpace(entry.Headword) ||
                string.IsNullOrWhiteSpace(entry.RawLine))
                continue;

            yield return entry;
        }
    }

    private string ExtractSimpleHeadword(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Method 1: If contains ⬄, extract before it
        if (line.Contains('⬄'))
        {
            var idx = line.IndexOf('⬄');
            return line.Substring(0, idx).Trim();
        }

        // Method 2: Extract until first space, slash, or bracket
        var endIndex = line.IndexOfAny(new[] { ' ', '\t', '/', '[' });
        if (endIndex <= 0)
            endIndex = line.Length;

        var headword = line.Substring(0, endIndex).Trim();

        // Basic validation - must start with letter or number
        if (string.IsNullOrEmpty(headword) || !char.IsLetterOrDigit(headword[0]))
            return null;

        return headword;
    }

    private string ExtractHeadwordFromEngChnLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // For test "A, B, C ⬄ test" - we need to extract "A, B, C"
        // The current logic might be rejecting it

        string potentialHeadword = null;

        // 1. If line contains ⬄ separator, extract headword before it
        if (line.Contains('⬄'))
        {
            var idx = line.IndexOf('⬄');
            potentialHeadword = line.Substring(0, idx).Trim();
        }
        else
        {
            // 2. Extract first word (until first space or special character)
            // But for "A, B, C" we want the whole thing until space after C
            var match = Regex.Match(line, @"^([^\[\/\s]+)");
            if (match.Success)
            {
                potentialHeadword = match.Groups[1].Value.Trim();
            }
            else
            {
                var endIndex = line.IndexOfAny(new[] { ' ', '\t', '/', '[' });
                if (endIndex <= 0)
                    endIndex = line.Length;

                potentialHeadword = line.Substring(0, endIndex).Trim();
            }

            // Clean up any trailing punctuation but keep commas
            potentialHeadword = potentialHeadword.TrimEnd('.', ';', ':', '!', '?', '·');
        }

        // Validate it's a proper headword - be less restrictive
        if (!IsValidEngChnHeadword(potentialHeadword))
            return null;

        return potentialHeadword;
    }

    private bool IsValidEngChnHeadword(string headword)
    {
        if (string.IsNullOrWhiteSpace(headword) || headword.Length > 100)
            return false;

        // Be more permissive - allow comma-separated headwords like "A, B, C"

        // Check if it starts with a letter or number
        if (!char.IsLetterOrDigit(headword[0]))
            return false;

        // Check if it contains at least one letter or is a valid pattern
        if (headword.Any(char.IsLetter))
            return true;

        // Allow numeric patterns
        if (Regex.IsMatch(headword, @"^\d+$")) // Pure numbers
            return true;

        if (Regex.IsMatch(headword, @"^\d+[-\/]\d+$")) // e.g., "24-7", "24/7"
            return true;

        if (Regex.IsMatch(headword, @"^\d+[A-Za-z]$")) // e.g., "3D", "4G"
            return true;

        // Allow comma-separated letters (e.g., "A, B, C")
        if (Regex.IsMatch(headword, @"^[A-Za-z](?:,\s*[A-Za-z])+$"))
            return true;

        return false;
    }

    private bool IsValidNumericHeadword(string headword)
    {
        // Allow specific numeric headwords that are valid dictionary entries
        var validNumericHeadwords = new HashSet<string>
        {
            "911", "999", "24-7", "24/7", "360", "3D", "4G", "5G", "2D", "3G"
        };

        // Check if it's in the allowed list or matches common patterns
        if (validNumericHeadwords.Contains(headword))
            return true;

        // Allow common patterns like "24-7", "360-degree", etc.
        if (Regex.IsMatch(headword, @"^\d+[-\/]\d+$")) // e.g., "24-7", "24/7"
            return true;

        if (Regex.IsMatch(headword, @"^\d+[A-Za-z]$")) // e.g., "3D", "4G"
            return true;

        return false;
    }

    private static bool ValidateEnglishChineseEntry(EnglishChineseRawEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.Headword) &&
               !string.IsNullOrWhiteSpace(entry.RawLine) &&
               entry.Headword.Length <= 100 &&
               entry.RawLine.Length <= 8000;
    }
}