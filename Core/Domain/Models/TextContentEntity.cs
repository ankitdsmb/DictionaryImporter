namespace DictionaryImporter.Core.Domain.Models;

public abstract class TextContentEntity
{
    public bool HasNonEnglishText { get; set; }
    public long? NonEnglishTextId { get; set; }
    public string SourceCode { get; set; } = null!;

    // Helper method to get the actual text (English or placeholder)
    public string GetDisplayText(string storedText, Func<long, string> nonEnglishTextLoader)
    {
        if (HasNonEnglishText && NonEnglishTextId.HasValue)
        {
            // Return placeholder from main table
            return storedText;
        }
        return storedText;
    }

    // Helper method to get original non-English text
    public string? GetOriginalNonEnglishText(Func<long, string> nonEnglishTextLoader)
    {
        if (HasNonEnglishText && NonEnglishTextId.HasValue)
        {
            return nonEnglishTextLoader(NonEnglishTextId.Value);
        }
        return null;
    }
}