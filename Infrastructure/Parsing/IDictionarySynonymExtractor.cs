namespace DictionaryImporter.Infrastructure.Parsing;

public interface IDictionarySynonymExtractor
{
    IEnumerable<SynonymResult> Extract(string word, string definition, string rawFragment);

    bool ValidateSynonymPair(string sourceWord, string targetHeadword);
}