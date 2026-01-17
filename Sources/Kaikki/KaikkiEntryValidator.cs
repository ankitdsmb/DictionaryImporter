namespace DictionaryImporter.Sources.Kaikki
{
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

            if (entry.Word.Length > 200)
                return ValidationResult.Invalid("Word too long");

            if (entry.Definition.Length < 3)
                return ValidationResult.Invalid("Definition too short");

            if (string.IsNullOrWhiteSpace(entry.SourceCode))
                return ValidationResult.Invalid("SourceCode missing");

            if (entry.SenseNumber <= 0)
                return ValidationResult.Invalid("Invalid sense number");

            return ValidationResult.Valid();
        }
    }
}