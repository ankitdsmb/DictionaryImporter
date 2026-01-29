using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Infrastructure.Parsing;

public interface ISourceSpecificProcessor
{
    bool CanHandle(string sourceCode);

    Task<ProcessingResult> ProcessEntryAsync(
        DictionaryEntry entry,
        ParsedDefinition parsed,
        long parsedId,
        CancellationToken ct);
}