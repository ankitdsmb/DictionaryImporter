namespace DictionaryImporter.Core.Validation
{
    public interface IDictionaryEntryValidator
    {
        ValidationResult Validate(
            Domain.Models.DictionaryEntry entry);
    }
}
