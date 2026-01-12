// Add this interface in DictionaryImporter.Core.Persistence namespace

namespace DictionaryImporter.Core.Persistence;

public interface IDictionaryEntryExampleWriter
{
    Task WriteAsync(
        long parsedDefinitionId,
        string exampleText,
        string sourceCode,
        CancellationToken ct);
}