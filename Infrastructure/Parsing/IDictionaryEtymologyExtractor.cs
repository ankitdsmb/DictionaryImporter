namespace DictionaryImporter.Infrastructure.Parsing;

public interface IDictionaryEtymologyExtractor
{
    EtymologyResult Extract(string word, string definition, string rawFragment);
}