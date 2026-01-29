using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.Validation;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordEntryValidator : IDictionaryEntryValidator
{
    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return ValidationResult.Invalid("Definition missing");

        if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
            return ValidationResult.Invalid("NormalizedWord missing");

        if (entry.SenseNumber <= 0)
            return ValidationResult.Invalid("Invalid sense number");

        return ValidationResult.Valid();
    }
}