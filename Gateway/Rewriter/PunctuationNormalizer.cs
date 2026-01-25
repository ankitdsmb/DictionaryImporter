namespace DictionaryImporter.Gateway.Rewriter
{
    public static class PunctuationNormalizer
    {
        private static readonly RegexOptions Opt =
            RegexOptions.Compiled | RegexOptions.CultureInvariant;

        private static readonly Regex MultiSpace = new(@"\s{2,}", Opt);

        private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.;:!?])", Opt);

        private static readonly Regex MissingSpaceAfterCommaSemicolonColon = new(@"([,;:])([^\s\)\]\}""'“”’])", Opt);

        private static readonly Regex MissingSpaceAfterPeriodQuestionExclamation =
            new(@"([.!?])([A-Za-z])", Opt);

        private static readonly Regex RepeatedQuestion = new(@"\?{2,}", Opt);

        private static readonly Regex RepeatedExclamation = new(@"!{2,}", Opt);

        private static readonly Regex RepeatedDots = new(@"\.{4,}", Opt); // allow "..." but normalize "...." -> "..."

        private static readonly Regex SpaceInsideParensOpen = new(@"\(\s+", Opt);

        private static readonly Regex SpaceInsideParensClose = new(@"\s+\)", Opt);

        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            try
            {
                var s = input;

                // 1) Collapse multiple spaces (do not touch newlines/tabs too aggressively)
                s = MultiSpace.Replace(s, " ");

                // 2) Remove spaces before punctuation: "word ," -> "word,"
                s = SpaceBeforePunctuation.Replace(s, "$1");

                // 3) Ensure space after comma/semicolon/colon: "a,b" -> "a, b"
                s = MissingSpaceAfterCommaSemicolonColon.Replace(s, "$1 $2");

                // 4) Ensure space after .!? when followed by a letter: "word.Next" -> "word. Next"
                s = MissingSpaceAfterPeriodQuestionExclamation.Replace(s, "$1 $2");

                // 5) Normalize repeated punctuation
                s = RepeatedQuestion.Replace(s, "?");
                s = RepeatedExclamation.Replace(s, "!");
                s = RepeatedDots.Replace(s, "...");

                // 6) Normalize spaces inside parentheses: "( word )" -> "(word)"
                s = SpaceInsideParensOpen.Replace(s, "(");
                s = SpaceInsideParensClose.Replace(s, ")");

                // 7) Final trim
                return s.Trim();
            }
            catch
            {
                // Never crash pipeline
                return input;
            }
        }
    }
}
