namespace DictionaryImporter.AITextKit.Grammar.Core;

public interface ILanguageDetector
{
    string Detect(string text);
}