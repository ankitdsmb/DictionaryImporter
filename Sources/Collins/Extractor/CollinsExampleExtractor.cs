using DictionaryImporter.Common.SourceHelper;

namespace DictionaryImporter.Sources.Collins.Extractor;

public sealed class CollinsExampleExtractor : IExampleExtractor
{
    public string SourceCode => "ENG_COLLINS";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        if (parsed == null)
            return new List<string>();

        // First, check if examples are already in the parsed definition
        if (parsed.Examples?.Any() == true)
        {
            return parsed.Examples;
        }

        // If not, extract from raw fragment
        var raw = parsed.RawFragment ?? parsed.Definition;

        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        // Use the improved extraction from ParsingHelperCollins
        return ParsingHelperCollins.ExtractCollinsExamples(raw)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Where(e => e.Length > 10)
            .Where(e => IsValidExample(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsValidExample(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 15)
            return false;

        // Must contain English letters
        if (!Regex.IsMatch(text, @"[A-Za-z]"))
            return false;

        // Must look like a sentence
        if (!char.IsUpper(text[0]))
            return false;

        // Must end with proper punctuation
        if (!text.EndsWith(".") && !text.EndsWith("!") && !text.EndsWith("?"))
            return false;

        // Not a definition
        if (text.StartsWith("If ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("To ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("A ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("An ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(" means ") ||
            text.Contains(" is ") ||
            text.Contains(" are "))
        {
            return false;
        }

        return true;
    }
}