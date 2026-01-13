namespace DictionaryImporter.Sources.Kaikki;

public sealed class KaikkiEntryValidator : IDictionaryEntryValidator
{
    public ValidationResult Validate(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return ValidationResult.Invalid("Word missing");

        if (string.IsNullOrWhiteSpace(entry.Definition))
            return ValidationResult.Invalid("Definition missing");

        if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
            return ValidationResult.Invalid("NormalizedWord missing");

        // Kaikki entries might be very short (single characters, symbols)
        // So we relax the length constraints
        if (entry.Word.Length > 200)
            return ValidationResult.Invalid("Word too long");

        if (entry.Definition.Length < 3)
            return ValidationResult.Invalid("Definition too short");

        // Some entries might not have letters (like symbols, numbers)
        // So we don't enforce the "contains letters" rule

        if (string.IsNullOrWhiteSpace(entry.SourceCode))
            return ValidationResult.Invalid("SourceCode missing");

        if (entry.SenseNumber <= 0)
            return ValidationResult.Invalid("Invalid sense number");

        return ValidationResult.Valid();
    }
}