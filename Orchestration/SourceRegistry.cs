using DictionaryImporter.Core.Sources;
using DictionaryImporter.Sources.Collins;
using DictionaryImporter.Sources.Oxford;
namespace DictionaryImporter.Orchestration
{
    public static class SourceRegistry
    {
        public static IEnumerable<IDictionarySourceModule> CreateSources()
        {
            //yield return new GutenbergWebsterSourceModule();
            //yield return new CollinsSourceModule();
            yield return new OxfordSourceModule();
            yield return new OxfordSourceModule();
            //yield return new StructuredJsonSourceModule();
            //yield return new EnglishChineseSourceModule();
        }
    }
}