using DictionaryImporter.Infrastructure.Validation;

namespace DictionaryImporter.Sources.Collins
{
    public sealed class CollinsEntryValidator : IDictionaryEntryValidator
    {
        public ValidationResult Validate(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
                return ValidationResult.Invalid("Word missing");

            if (string.IsNullOrWhiteSpace(entry.Definition))
                return ValidationResult.Invalid("Definition missing");

            // Collins entries should have special formatting
            if (!string.IsNullOrWhiteSpace(entry.RawFragment) &&
                (entry.RawFragment.Contains("★") || entry.RawFragment.Contains("●")))
                return ValidationResult.Valid();

            return ValidationResult.Valid(); // Be lenient
        }
    }
}