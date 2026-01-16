namespace DictionaryImporter.AITextKit.Grammar;

public interface ISpellChecker
{
    bool IsSupported { get; }

    SpellCheckResult Check(string word);
}