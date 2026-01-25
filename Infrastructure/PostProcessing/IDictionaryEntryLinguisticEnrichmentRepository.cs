using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Infrastructure.PostProcessing;

public interface IDictionaryEntryLinguisticEnrichmentRepository
{
    Task<long> ExtractSynonymsFromCrossReferencesAsync(
        string sourceCode,
        CancellationToken ct);

    Task<IReadOnlyList<CanonicalWordIpaCandidateRow>> GetIpaCandidatesFromParsedFragmentsAsync(
        string sourceCode,
        CancellationToken ct);

    Task<int> InsertCanonicalWordPronunciationIfMissingAsync(
        long canonicalWordId,
        string localeCode,
        string ipa,
        CancellationToken ct);
}

public sealed class CanonicalWordIpaCandidateRow
{
    public long CanonicalWordId { get; init; }
    public string RawFragment { get; init; } = string.Empty;
}