using DictionaryImporter.Core.Pipeline;
using DictionaryImporter.Core.Validation;

namespace DictionaryImporter.Core.Abstractions;

public interface IImportEngineRegistry
{
    IImportEngine CreateEngine(
        string sourceCode,
        IDictionaryEntryValidator validator);
}