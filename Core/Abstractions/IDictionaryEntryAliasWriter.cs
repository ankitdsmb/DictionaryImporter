namespace DictionaryImporter.Core.Abstractions;

public interface IDictionaryEntryAliasWriter
{
    Task WriteAsync(long parsedDefinitionId, string aliasText, string sourceCode, CancellationToken ct);
}