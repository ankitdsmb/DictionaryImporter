using DictionaryImporter.Gateway.Grammar.Core.Results;

namespace DictionaryImporter.Gateway.Grammar.Core;

public interface ISpellChecker
{
    bool IsSupported { get; }

    SpellCheckResult Check(string word);
}