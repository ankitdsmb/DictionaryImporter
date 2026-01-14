namespace DictionaryImporter.Core.Grammar;

public interface ILanguageDetector
{
    string Detect(string text);
}