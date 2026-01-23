using DictionaryImporter.Infrastructure.Validation;

namespace DictionaryImporter.Core.Abstractions
{
    public interface IDictionaryEntryValidator
    {
        ValidationResult Validate(
            DictionaryEntry entry);
    }
}