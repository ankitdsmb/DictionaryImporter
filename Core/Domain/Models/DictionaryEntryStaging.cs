using DictionaryImporter.Infrastructure.FragmentStore;

namespace DictionaryImporter.Core.Domain.Models;

public class DictionaryEntryStaging
{
    public string Word { get; set; } = null!;
    public string NormalizedWord { get; set; } = null!;
    public string? PartOfSpeech { get; set; }
    public string? Definition { get; set; }
    public string? Etymology { get; set; }
    public string? RawFragment { get; set; }
    private string? _rawFragment;

    private readonly object _lock = new();

    public string RawFragmentLine
    {
        get
        {
            if (_rawFragment != null)
                return _rawFragment;

            lock (_lock)
            {
                _rawFragment ??= RawFragments.Read(RawFragment, SourceCode, Word);
            }

            return _rawFragment;
        }
    }

    public int SenseNumber { get; set; } = 1;
    public string SourceCode { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }
    public byte[] WordHashBytes { get; internal set; } = null!;
    public byte[] DefinitionHashBytes { get; internal set; } = null!;
}