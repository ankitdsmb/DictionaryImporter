namespace DictionaryImporter.Core.Abstractions;

public interface IDictionaryEntryPartOfSpeechRepository
{
    Task PersistHistoryAsync(string sourceCode, CancellationToken ct);
    Task<IReadOnlyList<(long EntryId, string Definition)>> GetEntriesNeedingPosAsync(
        string sourceCode,
        CancellationToken ct);

    Task<int> UpdatePartOfSpeechIfUnknownAsync(
        long entryId,
        string pos,
        int confidence,
        CancellationToken ct);

    Task<int> BackfillConfidenceAsync(
        string sourceCode,
        CancellationToken ct);
}