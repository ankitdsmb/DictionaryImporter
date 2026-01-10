using DictionaryImporter.Core.Sources;
using DictionaryImporter.Sources.EnglishChinese;
using DictionaryImporter.Sources.GutenbergWebster;
using DictionaryImporter.Sources.StructuredJson;
namespace DictionaryImporter.Orchestration
{
    public static class SourceRegistry
    {
        public static IEnumerable<IDictionarySourceModule> CreateSources()
        {
            yield return new GutenbergWebsterSourceModule();
            //yield return new StructuredJsonSourceModule();
            //yield return new EnglishChineseSourceModule();
        }
    }
}