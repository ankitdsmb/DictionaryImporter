namespace DictionaryImporter.Core.Validation
{
    public interface IDictionaryEntryValidator
    {
        ValidationResult Validate(
            DictionaryEntry entry);
    }
}