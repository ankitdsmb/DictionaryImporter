using DictionaryImporter.Sources.Common;

namespace DictionaryImporter.Sources.EnglishChinese
{
    public sealed class EnglishChineseEntryValidator : IDictionaryEntryValidator
    {
        public ValidationResult Validate(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
                return ValidationResult.Invalid("Word missing");

            if (string.IsNullOrWhiteSpace(entry.Definition))
                return ValidationResult.Invalid("Definition missing");

            if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
                return ValidationResult.Invalid("NormalizedWord missing");

            return ValidationResult.Valid();
        }
    }
}