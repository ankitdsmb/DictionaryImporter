using DictionaryImporter.Core.Pipeline;

namespace DictionaryImporter.Core.Abstractions;

public interface IImportEngineRegistry
{
    IImportEngine CreateEngine(
        string sourceCode,
        IDictionaryEntryValidator validator);
}