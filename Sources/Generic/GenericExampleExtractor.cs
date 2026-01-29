using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.Generic;

public sealed class GenericExampleExtractor : IExampleExtractor
{
    public string SourceCode => "*";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(parsed.Definition))
            return examples;

        var matches = Regex.Matches(
            parsed.Definition,
            @"[\""“”']([^\""“”]+)[\""“”']",
            RegexOptions.Compiled);

        foreach (Match match in matches)
        {
            var raw = match.Groups[1].Value;

            if (raw.Length < 10)
                continue;

            var normalized = raw.NormalizeExample();

            if (!normalized.IsValidExampleSentence())
                continue;

            examples.Add(normalized);
        }

        return examples
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}