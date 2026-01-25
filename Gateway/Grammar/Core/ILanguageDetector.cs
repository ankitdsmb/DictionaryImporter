namespace DictionaryImporter.Gateway.Grammar.Core;

public interface ILanguageDetector
{
    string Detect(string text);
}