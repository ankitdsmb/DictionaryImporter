using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Pipeline;
using DictionaryImporter.Core.Validation;

namespace DictionaryImporter.Orchestration
{
    public interface IImportEngineRegistry
    {
        IImportEngine CreateEngine(
            string sourceCode,
            IDictionaryEntryValidator validator);
    }
}