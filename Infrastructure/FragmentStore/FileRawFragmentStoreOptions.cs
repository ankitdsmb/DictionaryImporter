using System.IO.Compression;

namespace DictionaryImporter.Infrastructure.FragmentStore;

public sealed class FileRawFragmentStoreOptions
{
    public int MaxMemoryCacheSize { get; init; } = 100_000;
    public int CompressionThreshold { get; init; } = 1024;
    public int DirectoryDepth { get; init; } = 2;
    public int PrefixLength { get; init; } = 1;
    public bool CaseSensitive { get; init; }
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Fastest;
}