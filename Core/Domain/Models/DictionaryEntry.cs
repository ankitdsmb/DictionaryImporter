using DictionaryImporter.Infrastructure.FragmentStore;

namespace DictionaryImporter.Core.Domain.Models;

public class DictionaryEntry
{
    public long DictionaryEntryId { get; set; }
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
    public List<string> Examples { get; internal set; }
    public string? UsageNote { get; internal set; }
    public string? DomainLabel { get; internal set; }
    public string? GrammarInfo { get; internal set; }
    public string CrossReference { get; internal set; }
    public string Ipa { get; internal set; }
    public int? PartOfSpeechConfidence { get; internal set; }
}