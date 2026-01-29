using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Core.Abstractions;

public interface IEntryEtymologyWriter
{
    Task WriteAsync(DictionaryEntryEtymology etymology, CancellationToken ct);
}