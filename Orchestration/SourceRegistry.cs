using DictionaryImporter.Infrastructure.Source;
using DictionaryImporter.Sources.Century21;
using DictionaryImporter.Sources.Collins;
using DictionaryImporter.Sources.EnglishChinese;
using DictionaryImporter.Sources.Gutenberg;
using DictionaryImporter.Sources.Kaikki;
using DictionaryImporter.Sources.Oxford;
using DictionaryImporter.Sources.StructuredJson;

namespace DictionaryImporter.Orchestration;

public static class SourceRegistry
{
    public static IEnumerable<IDictionarySourceModule> CreateSources()
    {
        yield return new GutenbergWebsterSourceModule();
        yield return new CollinsSourceModule();
        yield return new OxfordSourceModule();
        yield return new StructuredJsonSourceModule();
        yield return new EnglishChineseSourceModule();
        yield return new Century21SourceModule();
        yield return new KaikkiSourceModule();
    }
}