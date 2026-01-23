// File: Core/Persistence/IDictionaryEntryExampleWriter.cs
namespace DictionaryImporter.Core.Persistence
{
    public interface IDictionaryEntryExampleWriter
    {
        Task WriteAsync(long dictionaryEntryParsedId, string exampleText, string sourceCode, CancellationToken ct);
    }
}