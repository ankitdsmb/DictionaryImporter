using DictionaryImporter.Gateway.Grammar.Core;
using NTextCat;
using System.Collections.Concurrent;

namespace DictionaryImporter.Gateway.Grammar.Engines;

public sealed class NTextCatLangDetector : INTextCatLangDetector, IDisposable
{
    private const int DefaultPoolSize = 4;

    private readonly ConcurrentBag<RankedLanguageIdentifier> _pool = new();
    private readonly SemaphoreSlim _poolGate;
    private readonly string? _profilePath;
    private int _disposed;

    public NTextCatLangDetector(int poolSize = DefaultPoolSize)
    {
        if (poolSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(poolSize));

        _poolGate = new SemaphoreSlim(poolSize, poolSize);

        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Gateway",
            "Grammar",
            "Configuration",
            "Core14.profile.xml");

        if (!File.Exists(path))
        {
            _profilePath = null;
            return;
        }

        _profilePath = path;

        // Warm the pool
        var factory = new RankedLanguageIdentifierFactory();
        for (var i = 0; i < poolSize; i++)
        {
            _pool.Add(factory.Load(_profilePath));
        }
    }

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
            return "en-US";

        if (_profilePath == null)
            return "en-US";

        ThrowIfDisposed();

        _poolGate.Wait();
        RankedLanguageIdentifier? identifier = null;

        try
        {
            if (!_pool.TryTake(out identifier))
            {
                // Should be rare; safety fallback
                var factory = new RankedLanguageIdentifierFactory();
                identifier = factory.Load(_profilePath);
            }

            var best = identifier.Identify(text).FirstOrDefault();
            var iso639_3 = best?.Item1?.Iso639_3;

            return iso639_3 switch
            {
                "eng" => "en-US",
                "fra" => "fr-FR",
                "deu" => "de-DE",
                "spa" => "es-ES",
                "ita" => "it-IT",
                _ => "en-US"
            };
        }
        catch
        {
            return "en-US";
        }
        finally
        {
            if (identifier != null)
                _pool.Add(identifier);

            _poolGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(NTextCatLangDetector));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _poolGate.Dispose();
        _pool.Clear();
    }
}