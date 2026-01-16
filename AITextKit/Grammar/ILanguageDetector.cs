namespace DictionaryImporter.AITextKit.Grammar;

public interface ILanguageDetector
{
    string Detect(string text);
}