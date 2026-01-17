namespace DictionaryImporter.Core.Validation
{
    public sealed class DefaultDictionaryEntryValidator
        : IDictionaryEntryValidator
    {
        private const int MaxWordLength = 200;
        private const int MinDefinitionLength = 10;

        public ValidationResult Validate(
            DictionaryEntry e)
        {
            if (string.IsNullOrWhiteSpace(e.Word))
                return ValidationResult.Invalid("Word is empty");

            if (e.Word.Length > MaxWordLength)
                return ValidationResult.Invalid("Word too long");

            if (string.IsNullOrWhiteSpace(e.NormalizedWord))
                return ValidationResult.Invalid("NormalizedWord missing");

            if (string.IsNullOrWhiteSpace(e.Definition))
                return ValidationResult.Invalid("Definition empty");

            if (e.Definition.Length < MinDefinitionLength)
                return ValidationResult.Invalid("Definition too short");

            if (!e.Word.Any(char.IsLetter))
                return ValidationResult.Invalid("Word contains no letters");

            if (string.IsNullOrWhiteSpace(e.SourceCode))
                return ValidationResult.Invalid("SourceCode missing");

            if (e.SenseNumber <= 0)
                return ValidationResult.Invalid("Invalid sense number");

            return ValidationResult.Valid();
        }
    }
}