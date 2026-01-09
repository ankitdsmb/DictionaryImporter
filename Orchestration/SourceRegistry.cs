using DictionaryImporter.Core.Sources;
using DictionaryImporter.Sources.GutenbergWebster;
using DictionaryImporter.Sources.StructuredJson;

namespace DictionaryImporter.Orchestration
{
    public static class SourceRegistry
    {
        public static IReadOnlyList<IDictionarySourceModule> CreateSources()
        {
            return new IDictionarySourceModule[]
            {
                new GutenbergWebsterSourceModule(),
                new StructuredJsonSourceModule()
            };
        }
    }
}
