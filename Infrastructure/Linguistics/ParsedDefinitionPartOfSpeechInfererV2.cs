using DictionaryImporter.Core.Linguistics;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Infrastructure.Linguistics
{
    public sealed class ParsedDefinitionPartOfSpeechInfererV2
        : IPartOfSpeechInfererV2
    {
        public PartOfSpeechResult InferWithConfidence(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return new() { Pos = "unk", Confidence = 0 };

            var text = definition.Trim();

            // 1. Explicit POS tokens (highest confidence)
            if (Regex.IsMatch(text, @"\b(noun|n\.)\b", RegexOptions.IgnoreCase))
                return new() { Pos = "noun", Confidence = 95 };

            if (Regex.IsMatch(text, @"\b(verb|v\.)\b", RegexOptions.IgnoreCase))
                return new() { Pos = "verb", Confidence = 95 };

            if (Regex.IsMatch(text, @"\b(adjective|adj\.)\b", RegexOptions.IgnoreCase))
                return new() { Pos = "adj", Confidence = 90 };

            if (Regex.IsMatch(text, @"\b(adverb|adv\.)\b", RegexOptions.IgnoreCase))
                return new() { Pos = "adv", Confidence = 90 };

            // 2. Structure heuristics
            if (text.StartsWith("To ", StringComparison.OrdinalIgnoreCase))
                return new() { Pos = "verb", Confidence = 80 };

            if (StartsWithAny(text, "A ", "An ", "The ", "One who "))
                return new() { Pos = "noun", Confidence = 80 };

            if (text.StartsWith("Of or relating to ", StringComparison.OrdinalIgnoreCase))
                return new() { Pos = "adj", Confidence = 75 };

            // 3. Weak fallback
            return new() { Pos = "unk", Confidence = 30 };
        }

        private static bool StartsWithAny(string text, params string[] values)
        {
            foreach (var v in values)
            {
                if (text.StartsWith(v, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
