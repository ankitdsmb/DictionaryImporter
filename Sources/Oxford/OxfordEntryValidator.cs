namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordEntryValidator : IDictionaryEntryValidator
{
    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return ValidationResult.Invalid("Definition missing");

        if (entry.SenseNumber <= 0)
            return ValidationResult.Invalid("Invalid sense number");

        return ValidationResult.Valid();
    }
}