using NHunspell;

namespace DictionaryImporter.Core.Grammar;

public sealed class NHunspellSpellChecker : ISpellChecker
{
    private readonly Hunspell _hunspell;

    public NHunspellSpellChecker(string languageCode)
    {
        // Map languageCode to dictionary and affix files
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
            return new SpellCheckResult(false, Array.Empty<string>());

        var correct = _hunspell.Spell(word);
        var suggestions = correct ? Array.Empty<string>() : _hunspell.Suggest(word).ToArray();

        return new SpellCheckResult(correct, suggestions);
    }

    private string GetDictionaryPath(string languageCode)
    {
        // We can store dictionaries in a folder like "Dictionaries"
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries", $"{languageCode}.dic");
    }

    private string GetAffixPath(string languageCode)
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries", $"{languageCode}.aff");
    }
}

public interface ISpellChecker
{
    bool IsSupported { get; }

    SpellCheckResult Check(string word);
}

public record SpellCheckResult(bool IsCorrect, IReadOnlyList<string> Suggestions);