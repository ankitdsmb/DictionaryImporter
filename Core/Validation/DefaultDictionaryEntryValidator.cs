using System;
using System.Linq;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Sources.Common.Helper;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Validation
{
    public sealed class DefaultDictionaryEntryValidator(ILogger<DefaultDictionaryEntryValidator> logger)
        : IDictionaryEntryValidator
    {
        public ValidationResult Validate(DictionaryEntry entry)
        {
            if (entry == null)
                return ValidationResult.Invalid("Entry is null");

            // Validate Word
            if (string.IsNullOrWhiteSpace(entry.Word))
                return ValidationResult.Invalid("Word missing");

            var word = entry.Word.Trim();
            if (word.Length < 1)
                return ValidationResult.Invalid("Word too short");

            // Validate SourceCode (optional, but helps for normalization rules)
            var sourceCode = entry.SourceCode ?? string.Empty;

            // Validate NormalizedWord (generate if missing)
            if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
            {
                // ✅ FIX: use helper (previous code called NormalizeWord() which didn't exist)
                entry.NormalizedWord =
                    Helper.NormalizeWordPreservingLanguage(entry.Word, sourceCode);

                if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
                    return ValidationResult.Invalid("NormalizedWord missing");
            }

            // Validate Definition (more lenient for Kaikki)
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                // For Kaikki, RawFragment might contain the definition
                if (string.Equals(entry.SourceCode, "KAIKKI", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(entry.RawFragment))
                {
                    // Try to extract definition from RawFragment
                    var definitions = ParsingHelperKaikki.ExtractEnglishDefinitions(entry.RawFragment);
                    if (definitions.Count > 0)
                        entry.Definition = definitions.First();
                }

                if (string.IsNullOrWhiteSpace(entry.Definition))
                    return ValidationResult.Invalid("Definition missing");
            }

            // ✅ use source-aware normalization for definition checks
            var definition = Helper.NormalizeDefinitionForSource(entry.Definition, sourceCode).Trim();

            // Single-letter words (like "A", "I") can have short definitions
            if (word.Length == 1 && definition.Length >= 3)
                return ValidationResult.Valid();

            // Common short words might have brief definitions
            var commonShortWords = new[] { "a", "i", "an", "be", "is", "am", "to", "do", "go", "no", "so" };
            if (commonShortWords.Contains(word.ToLowerInvariant()) && definition.Length >= 3)
                return ValidationResult.Valid();

            // Default minimum length
            if (definition.Length < 5)
            {
                logger.LogDebug(
                    "Entry rejected | Word={Word} | Source={Source} | Definition={Definition}",
                    word, entry.SourceCode, definition);

                return ValidationResult.Invalid("Definition too short");
            }

            // Check for valid content
            if (!definition.Any(char.IsLetter))
                return ValidationResult.Invalid("Definition contains no letters");

            return ValidationResult.Valid();
        }
    }
}
