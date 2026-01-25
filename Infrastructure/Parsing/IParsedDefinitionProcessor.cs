namespace DictionaryImporter.Infrastructure.Parsing;

/// <summary>
/// Interface for parsed definition processors
/// </summary>
public interface IParsedDefinitionProcessor
{
    Task ExecuteAsync(string sourceCode, CancellationToken ct);
}