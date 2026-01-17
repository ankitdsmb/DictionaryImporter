using DictionaryImporter.AITextKit.Grammar.Core.Results;

namespace DictionaryImporter.AITextKit.Grammar.Core;

public interface ISpellChecker
{
    bool IsSupported { get; }

    SpellCheckResult Check(string word);
}