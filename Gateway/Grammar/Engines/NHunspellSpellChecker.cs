using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Results;
using NHunspell;

namespace DictionaryImporter.Gateway.Grammar.Engines;

public sealed class NHunspellSpellChecker : ISpellChecker, IDisposable
{
    private readonly Hunspell? _hunspell;
    private readonly object _lock = new();
    private int _disposed;

    public NHunspellSpellChecker(string languageCode)
    {
        var dictPath = GetDictionaryPath(languageCode);
        var affPath = GetAffixPath(languageCode);

        if (File.Exists(dictPath) && File.Exists(affPath))
        {
            _hunspell = new Hunspell(affPath, dictPath);
        }
    }

    public bool IsSupported => _hunspell != null;

    public SpellCheckResult Check(string word)
    {
        if (_hunspell == null)
            return new SpellCheckResult(false, []);

        if (string.IsNullOrWhiteSpace(word))
            return new SpellCheckResult(true, []);

        lock (_lock)
        {
            ThrowIfDisposed();

            var correct = _hunspell.Spell(word);

            if (correct)
                return new SpellCheckResult(true, []);

            var suggestions = _hunspell.Suggest(word).ToArray();
            return new SpellCheckResult(false, suggestions);
        }
    }

    private static string GetDictionaryPath(string languageCode)
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "AITextKit/Grammar/Configuration/Dictionaries",
            $"{languageCode}.dic");
    }

    private static string GetAffixPath(string languageCode)
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "AITextKit/Grammar/Configuration/Dictionaries",
            $"{languageCode}.aff");
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(NHunspellSpellChecker));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        lock (_lock)
        {
            _hunspell?.Dispose();
        }
    }
}