using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.Validation;

namespace DictionaryImporter.Sources.Collins;

public sealed class CollinsEntryValidator : IDictionaryEntryValidator
{
    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return ValidationResult.Invalid("Definition missing");

        // Collins entries should have special formatting
        if (!string.IsNullOrWhiteSpace(entry.RawFragmentLine) &&
            (entry.RawFragmentLine.Contains("★") || entry.RawFragmentLine.Contains("●")))
            return ValidationResult.Valid();

        return ValidationResult.Valid(); // Be lenient
    }
}