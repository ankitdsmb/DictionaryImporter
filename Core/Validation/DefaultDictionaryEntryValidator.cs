using DictionaryImporter.Sources.Common.Helper;

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

            // Validate NormalizedWord (generate if missing)
            if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
            {
                entry.NormalizedWord = TextNormalizer.NormalizeWord(entry.Word);
                if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
                    return ValidationResult.Invalid("NormalizedWord missing");
            }

            // Validate Definition (more lenient for Kaikki)
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                // For Kaikki, RawFragment might contain the definition
                if (entry.SourceCode == "KAIKKI" && !string.IsNullOrWhiteSpace(entry.RawFragment))
                {
                    // Try to extract definition from RawFragment
                    var definitions = JsonProcessor.ExtractEnglishDefinitions(entry.RawFragment);
                    if (definitions.Count > 0)
                    {
                        entry.Definition = definitions.First();
                    }
                }

                if (string.IsNullOrWhiteSpace(entry.Definition))
                    return ValidationResult.Invalid("Definition missing");
            }

            // Check definition length (more lenient for certain words)
            var definition = entry.Definition.Trim();

            // Single-letter words (like "A", "I") can have short definitions
            if (word.Length == 1 && definition.Length >= 3)
            {
                // Accept short definitions for single letters
                return ValidationResult.Valid();
            }

            // Common short words might have brief definitions
            var commonShortWords = new[] { "a", "i", "an", "be", "is", "am", "to", "do", "go", "no", "so" };
            if (commonShortWords.Contains(word.ToLowerInvariant()) && definition.Length >= 3)
            {
                return ValidationResult.Valid();
            }

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