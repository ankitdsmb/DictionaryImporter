namespace DictionaryImporter.Sources.Century21
{
    public sealed class Country21EntryValidator : IDictionaryEntryValidator
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

            if (entry.Definition.Length < 5)
                return ValidationResult.Invalid("Definition too short");

            return ValidationResult.Valid();
        }
    }
}