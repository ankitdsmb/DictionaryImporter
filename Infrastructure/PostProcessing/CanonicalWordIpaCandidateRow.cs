namespace DictionaryImporter.Infrastructure.PostProcessing;

public sealed class CanonicalWordIpaCandidateRow
{
    public long CanonicalWordId { get; init; }
    public string RawFragment { get; init; } = string.Empty;
}