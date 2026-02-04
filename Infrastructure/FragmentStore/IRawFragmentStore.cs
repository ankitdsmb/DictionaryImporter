namespace DictionaryImporter.Infrastructure.FragmentStore;

public interface IRawFragmentStore
{
    string? Save(string sourceCode, string rawFragment, Encoding encoding, string word);

    string Read(string sourceCode, string? rawFragmentId, string word);
}