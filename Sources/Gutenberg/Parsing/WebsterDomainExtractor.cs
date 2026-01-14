namespace DictionaryImporter.Sources.Gutenberg.Parsing;

internal static class WebsterDomainExtractor
{
    private static readonly Regex DomainRegex =
        new(
            @"^\(\s*(?<domain>[A-Za-z]{2,10}\.?)\s*\)",
            RegexOptions.Compiled);

    public static string? Extract(ref string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        var match = DomainRegex.Match(definition.Trim());
        if (!match.Success)
            return null;

        var domain = match.Groups["domain"].Value.TrimEnd('.');

        definition =
            definition.Substring(match.Length)
                .TrimStart();

        return domain;
    }
}