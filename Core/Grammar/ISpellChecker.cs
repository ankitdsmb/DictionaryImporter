namespace DictionaryImporter.Core.Grammar;

public interface ISpellChecker
{
    bool IsSupported { get; }

    SpellCheckResult Check(string word);
}