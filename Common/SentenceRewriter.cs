using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Common
{
    public enum RewriteMode
    {
        GrammarFix,
        Simplify,
        Formal,
        Casual,
        Shorten,
        Expand
    }

    public static class SentenceRewriter
    {
        private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforePunctRegex = new(@"\s+([,.;:!?])", RegexOptions.Compiled);
        private static readonly Regex RepeatPunctRegex = new(@"([!?\.])\1{1,}", RegexOptions.Compiled);

        public static string Rewrite(string input, RewriteMode mode)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input ?? string.Empty;

            try
            {
                var text = input.Trim();

                text = NormalizeSpacing(text);

                text = mode switch
                {
                    RewriteMode.GrammarFix => GrammarFix(text),
                    RewriteMode.Simplify => Simplify(text),
                    RewriteMode.Formal => MakeFormal(text),
                    RewriteMode.Casual => MakeCasual(text),
                    RewriteMode.Shorten => Shorten(text),
                    RewriteMode.Expand => Expand(text),
                    _ => text
                };

                text = NormalizeSpacing(text);
                text = FixSentenceCapitalization(text);

                return text;
            }
            catch
            {
                return input;
            }
        }

        private static string NormalizeSpacing(string text)
        {
            text = MultiSpaceRegex.Replace(text, " ");
            text = SpaceBeforePunctRegex.Replace(text, "$1");
            text = RepeatPunctRegex.Replace(text, "$1");
            return text.Trim();
        }

        private static string FixSentenceCapitalization(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Capitalize first alphabetic character
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i]))
                {
                    var chars = text.ToCharArray();
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    return new string(chars);
                }
            }

            return text;
        }

        private static string GrammarFix(string text)
        {
            text = ReplaceCommonMistakes(text);

            // "a apple" -> "an apple"
            text = Regex.Replace(
                text,
                @"\b(a)\s+([aeiouAEIOU])",
                "an $2",
                RegexOptions.Compiled);

            // "an car" -> "a car"
            text = Regex.Replace(
                text,
                @"\b(an)\s+([^aeiouAEIOU\W])",
                "a $2",
                RegexOptions.Compiled);

            // Ensure sentence ends with punctuation (basic)
            if (!Regex.IsMatch(text, @"[.!?]$"))
                text += ".";

            return text;
        }

        private static string Simplify(string text)
        {
            text = ReplaceCommonMistakes(text);

            // Replace complex phrases with simpler ones
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["utilize"] = "use",
                ["approximately"] = "about",
                ["commence"] = "start",
                ["terminate"] = "end",
                ["in order to"] = "to",
                ["due to the fact that"] = "because",
                ["at this point in time"] = "now",
                ["a majority of"] = "most",
                ["assist"] = "help",
                ["purchase"] = "buy"
            };

            text = ReplaceByWordMap(text, map);

            return EnsurePeriod(text);
        }

        private static string MakeFormal(string text)
        {
            text = ReplaceCommonMistakes(text);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["can't"] = "cannot",
                ["won't"] = "will not",
                ["don't"] = "do not",
                ["doesn't"] = "does not",
                ["didn't"] = "did not",
                ["isn't"] = "is not",
                ["aren't"] = "are not",
                ["wasn't"] = "was not",
                ["weren't"] = "were not",
                ["I'm"] = "I am",
                ["it's"] = "it is",
                ["that's"] = "that is",
                ["gonna"] = "going to",
                ["wanna"] = "want to",
                ["kinda"] = "somewhat"
            };

            text = ReplaceByWordMap(text, map);
            return EnsurePeriod(text);
        }

        private static string MakeCasual(string text)
        {
            text = ReplaceCommonMistakes(text);

            // Light casual tone (not too aggressive)
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["do not"] = "don't",
                ["does not"] = "doesn't",
                ["did not"] = "didn't",
                ["cannot"] = "can't",
                ["will not"] = "won't",
                ["I am"] = "I'm",
                ["it is"] = "it's",
                ["that is"] = "that's"
            };

            text = ReplaceByWordMap(text, map);
            return EnsurePeriod(text);
        }

        private static string Shorten(string text)
        {
            text = ReplaceCommonMistakes(text);

            // Remove filler words
            text = Regex.Replace(text, @"\b(very|really|actually|basically|literally|just)\b", "", RegexOptions.IgnoreCase);
            text = NormalizeSpacing(text);

            // Remove redundant phrase
            text = Regex.Replace(text, @"\b(in order to)\b", "to", RegexOptions.IgnoreCase);

            return EnsurePeriod(text);
        }

        private static string Expand(string text)
        {
            text = ReplaceCommonMistakes(text);

            // Add mild expansion if sentence is too short
            if (text.Length < 30 && !text.Contains(","))
            {
                text = text.TrimEnd('.', '!', '?') + ", which makes the meaning clearer.";
            }

            return EnsurePeriod(text);
        }

        private static string ReplaceCommonMistakes(string text)
        {
            // "i" as standalone pronoun -> "I"
            text = Regex.Replace(text, @"\b(i)\b", "I", RegexOptions.Compiled);

            // common typos
            text = Regex.Replace(text, @"\bteh\b", "the", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            text = Regex.Replace(text, @"\brecieve\b", "receive", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            text = Regex.Replace(text, @"\bdefinately\b", "definitely", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            return text;
        }

        private static string ReplaceByWordMap(string text, Dictionary<string, string> map)
        {
            foreach (var kvp in map)
            {
                // word-boundary replacement, case-insensitive
                text = Regex.Replace(
                    text,
                    $@"\b{Regex.Escape(kvp.Key)}\b",
                    kvp.Value,
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            return text;
        }

        private static string EnsurePeriod(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (!Regex.IsMatch(text, @"[.!?]$"))
                return text + ".";

            return text;
        }
    }
}
