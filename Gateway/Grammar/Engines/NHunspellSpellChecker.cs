using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Results;
using NHunspell;

namespace DictionaryImporter.Gateway.Grammar.Engines
{
    public sealed class NHunspellSpellChecker : ISpellChecker
    {
        private readonly Hunspell _hunspell;

        public NHunspellSpellChecker(string languageCode)
        {
            var dictPath = GetDictionaryPath(languageCode);
            var affPath = GetAffixPath(languageCode);

            if (File.Exists(dictPath) && File.Exists(affPath))
            {
                _hunspell = new Hunspell(affPath, dictPath);
            }
            else
            {
                _hunspell = null;
            }
        }

        public bool IsSupported => _hunspell != null;

        public SpellCheckResult Check(string word)
        {
            if (_hunspell == null)
                return new SpellCheckResult(false, []);

            var correct = _hunspell.Spell(word);
            var suggestions = correct ? [] : _hunspell.Suggest(word).ToArray();

            return new SpellCheckResult(correct, suggestions);
        }

        private string GetDictionaryPath(string languageCode)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AITextKit/Grammar/Configuration/Dictionaries", $"{languageCode}.dic");
        }

        private string GetAffixPath(string languageCode)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AITextKit/Grammar/Configuration/Dictionaries", $"{languageCode}.aff");
        }
    }
}