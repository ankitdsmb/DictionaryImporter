using DictionaryImporter.Core.Validation;
using DictionaryImporter.Domain.Models;

namespace DictionaryImporter.Sources.Collins
{
    public sealed class CollinsEntryValidator
        : IDictionaryEntryValidator
    {
        public ValidationResult Validate(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Word))
                return ValidationResult.Invalid("Word missing");

            if (string.IsNullOrWhiteSpace(entry.Definition))
                return ValidationResult.Invalid("Definition missing");

            return ValidationResult.Valid();
        }
    }
}