namespace DictionaryImporter.Core.Abstractions
{
    public interface IDictionaryEntryValidator
    {
        ValidationResult Validate(
            DictionaryEntry entry);
    }
}