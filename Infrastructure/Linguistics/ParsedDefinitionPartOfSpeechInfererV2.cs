using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Infrastructure.Linguistics;

public sealed class ParsedDefinitionPartOfSpeechInfererV2
    : IPartOfSpeechInfererV2
{
    public PartOfSpeechResult InferWithConfidence(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return new PartOfSpeechResult { Pos = "unk", Confidence = 0 };

        var text = definition.Trim();

        if (Regex.IsMatch(text, @"\b(noun|n\.)\b", RegexOptions.IgnoreCase))
            return new PartOfSpeechResult { Pos = "noun", Confidence = 95 };

        if (Regex.IsMatch(text, @"\b(verb|v\.)\b", RegexOptions.IgnoreCase))
            return new PartOfSpeechResult { Pos = "verb", Confidence = 95 };

        if (Regex.IsMatch(text, @"\b(adjective|adj\.)\b", RegexOptions.IgnoreCase))
            return new PartOfSpeechResult { Pos = "adj", Confidence = 90 };

        if (Regex.IsMatch(text, @"\b(adverb|adv\.)\b", RegexOptions.IgnoreCase))
            return new PartOfSpeechResult { Pos = "adv", Confidence = 90 };

        if (text.StartsWith("To ", StringComparison.OrdinalIgnoreCase))
            return new PartOfSpeechResult { Pos = "verb", Confidence = 80 };

        if (StartsWithAny(text, "A ", "An ", "The ", "One who "))
            return new PartOfSpeechResult { Pos = "noun", Confidence = 80 };

        if (text.StartsWith("Of or relating to ", StringComparison.OrdinalIgnoreCase))
            return new PartOfSpeechResult { Pos = "adj", Confidence = 75 };

        return new PartOfSpeechResult { Pos = "unk", Confidence = 30 };
    }

    private static bool StartsWithAny(string text, params string[] values)
    {
        foreach (var v in values)
            if (text.StartsWith(v, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}