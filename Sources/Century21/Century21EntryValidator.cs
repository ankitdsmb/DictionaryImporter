using DictionaryImporter.Infrastructure.Validation;

namespace DictionaryImporter.Sources.Century21;

public sealed class Century21EntryValidator : IDictionaryEntryValidator
{
    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return ValidationResult.Invalid("Definition missing");

        if (string.IsNullOrWhiteSpace(entry.RawFragment) || !entry.RawFragment.Contains("word_block"))
            return ValidationResult.Invalid("Invalid CENTURY21 HTML structure");

        return ValidationResult.Valid();
    }
}