using DictionaryImporter.Core.Grammar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Infrastructure.Grammar.Helper
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
                Replacements: replacements?.ToList() ?? new List<string>(),
                RuleId: ruleId,
                RuleDescription: ruleDescription,
                Tags: tags?.ToList() ?? new List<string> { "default" },
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

        public static GrammarIssue CreatePatternRuleIssue(
            int startOffset,
            int endOffset,
            string patternId,
            string description,
            string category,
            string replacement,
            string contextText,
            int confidenceLevel)
        {
            return Create(
                startOffset: startOffset,
                endOffset: endOffset,
                message: description,
                shortMessage: category,
                replacements: new List<string> { replacement },
                ruleId: $"PATTERN_{patternId}",
                ruleDescription: description,
                tags: new List<string> { category.ToLowerInvariant() },
                context: contextText,
                contextOffset: Math.Max(0, startOffset - 20),
                confidenceLevel: confidenceLevel
            );
        }

        public static GrammarIssue CreateRepeatedWordIssue(
            int startOffset,
            int endOffset,
            string word,
            int confidenceLevel = 90)
        {
            return new GrammarIssue(
                StartOffset: startOffset,
                EndOffset: endOffset,
                Message: $"Repeated word: '{word}'",
                ShortMessage: "Repeated word",
                Replacements: new List<string> { word },
                RuleId: "REPEATED_WORD",
                RuleDescription: "Repeated word detection",
                Tags: new List<string> { "style", "repetition" },
                Context: string.Empty,
                ContextOffset: 0,
                ConfidenceLevel: confidenceLevel
            );
        }
    }
}