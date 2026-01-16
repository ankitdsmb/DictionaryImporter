namespace DictionaryImporter.AITextKit.Grammar.Infrastructure.Helper
{
    internal static class GrammarIssueHelper
    {
        private static GrammarIssue Create(
        int startOffset,
        int endOffset,
        string message,
        string shortMessage,
        IEnumerable<string> replacements,
        string ruleId,
        string ruleDescription,
        IEnumerable<string>? tags = null,
        string? context = null,
        int? contextOffset = null,
        int confidenceLevel = 85)
        {
            return new GrammarIssue(
                StartOffset: startOffset,
                EndOffset: endOffset,
                Message: message,
                ShortMessage: shortMessage,
                Replacements: replacements?.ToList() ?? [],
                RuleId: ruleId,
                RuleDescription: ruleDescription,
                Tags: tags?.ToList() ?? ["default"],
                Context: context ?? string.Empty,
                ContextOffset: contextOffset ?? Math.Max(0, startOffset - 20),
                ConfidenceLevel: confidenceLevel
            );
        }

        public static GrammarIssue CreateSpellingIssue(
            int startOffset,
            int endOffset,
            string word,
            IEnumerable<string> suggestions,
            string contextText,
            int confidenceLevel = 85)
        {
            return Create(
                startOffset: startOffset,
                endOffset: endOffset,
                message: $"Possible spelling error: '{word}'",
                shortMessage: "Spelling",
                replacements: suggestions,
                ruleId: $"SPELLING_{word.ToUpperInvariant()}",
                ruleDescription: $"Spelling check for '{word}'",
                tags: new List<string> { "spelling" },
                context: contextText,
                contextOffset: Math.Max(0, startOffset - 20),
                confidenceLevel: confidenceLevel
            );
        }
    }
}