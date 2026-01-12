// DictionaryImporter/Sources/Oxford/OxfordEntryValidator.cs

using DictionaryImporter.Core.Validation;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordEntryValidator : IDictionaryEntryValidator
{
    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return ValidationResult.Invalid("Definition missing");

        // Oxford entries must have a sense number
        if (entry.SenseNumber <= 0)
            return ValidationResult.Invalid("Invalid sense number");

        return ValidationResult.Valid();
    }
}