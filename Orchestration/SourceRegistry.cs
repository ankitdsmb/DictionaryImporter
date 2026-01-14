using DictionaryImporter.Sources;
using DictionaryImporter.Sources.StructuredJson;

namespace DictionaryImporter.Orchestration;

public static class SourceRegistry
{
    public static IEnumerable<IDictionarySourceModule> CreateSources()
    {
        yield return new StructuredJsonSourceModule();
    }
}