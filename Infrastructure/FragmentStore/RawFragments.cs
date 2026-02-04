namespace DictionaryImporter.Infrastructure.FragmentStore;

public static class RawFragments
{
    private static IRawFragmentStore? _store;

    public static void Initialize(IRawFragmentStore store)
    {
        if (store is null)
            throw new ArgumentNullException(nameof(store));

        if (Interlocked.CompareExchange(ref _store, store, null) != null)
            throw new InvalidOperationException("RawFragments already initialized.");
    }

    public static string? Save(string sourceCode, string? rawFragment, Encoding encoding, string word)
        => Store.Save(sourceCode, rawFragment, encoding, word);

    public static string Read(string sourceCode, string rawFragmentId, string word)
        => Store.Read(sourceCode, rawFragmentId, word);

    private static IRawFragmentStore Store =>
        _store ?? throw new InvalidOperationException(
            "RawFragments not initialized. Call Initialize() during startup.");
}