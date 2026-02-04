using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace DictionaryImporter.Infrastructure.FragmentStore;

public sealed class FileRawFragmentStore : IRawFragmentStore, IDisposable
{
    private readonly string _rootPath;
    private readonly FileRawFragmentStoreOptions _options;

    private readonly ConcurrentDictionary<string, CacheItem> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _negativeCache = new();

    private readonly long _maxCacheBytes;
    private long _currentCacheBytes;

    private readonly Channel<WriteRequest> _writeChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;

    private volatile bool _disposed;

    private const int LargeBuffer = 256 * 1024; // 256 KB
    private const int SmallFragmentThresholdBytes = 1024; // 1 KB

    public FileRawFragmentStore(string rootPath, FileRawFragmentStoreOptions options)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _maxCacheBytes = (long)options.MaxMemoryCacheSize * 1024;
        Directory.CreateDirectory(_rootPath);

        _writeChannel = Channel.CreateBounded<WriteRequest>(
            new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _writerTask = Task.Run(WriterLoop);
    }

    // ============================
    // IRawFragmentStore
    // ============================

    public string? Save(string sourceCode, string? rawFragment, Encoding encoding, string word)
    {
        if (_disposed) return null;
        ArgumentException.ThrowIfNullOrEmpty(sourceCode);
        ArgumentNullException.ThrowIfNull(encoding);
        if (rawFragment is null) return null;

        var fragmentId = GenerateId(sourceCode, rawFragment);
        CacheSet(fragmentId, rawFragment);

        if (!_writeChannel.Writer.TryWrite(new WriteRequest(sourceCode, fragmentId, rawFragment, encoding)))
            _writeChannel.Writer.WriteAsync(new WriteRequest(sourceCode, fragmentId, rawFragment, encoding), _cts.Token).AsTask().Wait();

        return fragmentId;
    }

    // NOTE: `word` intentionally ignored
    public string Read(string sourceCode, string? rawFragmentId, string word)
    {
        if (_disposed) return string.Empty;
        if (!IsValidFragmentId(rawFragmentId)) return string.Empty;

        return ReadById(sourceCode, rawFragmentId!);
    }

    // ============================
    // Optimized ID-only read
    // ============================

    private string ReadById(string sourceCode, string fragmentId)
    {
        if (_cache.TryGetValue(fragmentId, out var cached))
            return cached.Value;

        if (_negativeCache.ContainsKey(fragmentId))
            return string.Empty;

        var path = GetFragmentPath(sourceCode, fragmentId);
        if (!File.Exists(path))
        {
            _negativeCache[fragmentId] = 0;
            return string.Empty;
        }

        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            LargeBuffer,
            FileOptions.SequentialScan);

        Stream stream = fs;
        if (path.EndsWith(".gz", StringComparison.Ordinal))
            stream = new GZipStream(fs, CompressionMode.Decompress);

        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false))
        {
            var content = reader.ReadToEnd();
            CacheSet(fragmentId, content);
            return content;
        }
    }

    // ============================
    // Background writer
    // ============================

    private async Task WriterLoop()
    {
        try
        {
            while (await _writeChannel.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_writeChannel.Reader.TryRead(out var req))
                {
                    if (_disposed) return;
                    WriteInternal(req);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void WriteInternal(WriteRequest req)
    {
        var isSmall = Encoding.UTF8.GetByteCount(req.Content) < SmallFragmentThresholdBytes;
        var path = GetFragmentPath(req.SourceCode, req.FragmentId, isSmall);

        if (File.Exists(path)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            LargeBuffer);

        if (isSmall)
        {
            using var writer = new StreamWriter(fs, req.Encoding);
            writer.Write(req.Content);
        }
        else
        {
            using var gzip = new GZipStream(fs, _options.CompressionLevel);
            using var writer = new StreamWriter(gzip, req.Encoding);
            writer.Write(req.Content);
        }
    }

    // ============================
    // Helpers
    // ============================

    private string GetFragmentPath(string sourceCode, string fragmentId, bool small = false)
    {
        return Path.Combine(
            _rootPath,
            sourceCode,
            fragmentId[..2],
            fragmentId[2..4],
            fragmentId + (small ? ".txt" : ".gz"));
    }

    private void CacheSet(string key, string value)
    {
        var size = Encoding.UTF8.GetByteCount(value);
        _cache[key] = new CacheItem(value, size);
        Interlocked.Add(ref _currentCacheBytes, size);

        if (_currentCacheBytes > _maxCacheBytes)
            EvictCache();
    }

    private void EvictCache()
    {
        foreach (var key in _cache.Keys.Take(_cache.Count / 4))
        {
            if (_cache.TryRemove(key, out var item))
                Interlocked.Add(ref _currentCacheBytes, -item.Size);
        }
    }

    private static bool IsValidFragmentId(string? id)
        => !string.IsNullOrWhiteSpace(id) && id.Length >= 8 && id.All(char.IsLetterOrDigit);

    private static string GenerateId(string sourceCode, string content)
    {
        Span<byte> buffer = stackalloc byte[512];
        var written = Encoding.UTF8.GetBytes(
            $"{sourceCode}|{content.AsSpan(0, Math.Min(200, content.Length))}",
            buffer);

        return Convert.ToHexString(SHA256.HashData(buffer[..written]))[..16];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _writeChannel.Writer.TryComplete();
        try { _writerTask.Wait(); } catch { }
        _cts.Dispose();
    }

    private sealed record CacheItem(string Value, int Size);

    private sealed record WriteRequest(
        string SourceCode,
        string FragmentId,
        string Content,
        Encoding Encoding);
}