namespace DictionaryImporter.Core.Persistence
{
    public interface IDictionaryEntryAliasWriter
    {
        Task WriteAsync(long parsedDefinitionId, string aliasText, string sourceCode, CancellationToken ct);
    }
    public interface IDictionaryEntryPartOfSpeechWriter
    {
        
    }

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

}