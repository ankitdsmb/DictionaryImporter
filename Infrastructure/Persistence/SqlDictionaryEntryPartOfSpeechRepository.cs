using DictionaryImporter.Common;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryPartOfSpeechRepository(
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlDictionaryEntryPartOfSpeechRepository> logger)
    : IDictionaryEntryPartOfSpeechRepository
{
    private readonly ISqlStoredProcedureExecutor _sp =
        sp ?? throw new ArgumentNullException(nameof(sp));

    private readonly ILogger<SqlDictionaryEntryPartOfSpeechRepository> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task PersistHistoryAsync(string sourceCode, CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        try
        {
            var rows = await _sp.ExecuteAsync(
                "sp_DictionaryEntryPartOfSpeech_PersistHistory",
                new { SourceCode = sourceCode },
                ct,
                timeoutSeconds: 60);

            _logger.LogInformation(
                "POS history persisted | SourceCode={SourceCode} | Rows={Rows}",
                sourceCode,
                rows);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to persist POS history | SourceCode={SourceCode}",
                sourceCode);
        }
    }

    public async Task<IReadOnlyList<(long EntryId, string Definition)>> GetEntriesNeedingPosAsync(
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        try
        {
            var rows = await _sp.QueryAsync<EntryNeedingPosRow>(
                "sp_DictionaryEntryPartOfSpeech_GetEntriesNeedingPos",
                new { SourceCode = sourceCode },
                ct,
                timeoutSeconds: 60);

            if (rows.Count == 0)
                return Array.Empty<(long EntryId, string Definition)>();

            return rows
                .Select(x => (x.EntryId, x.Definition ?? string.Empty))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to fetch entries needing POS | SourceCode={SourceCode}",
                sourceCode);

            return Array.Empty<(long EntryId, string Definition)>();
        }
    }

    public async Task<int> UpdatePartOfSpeechIfUnknownAsync(
        long entryId,
        string pos,
        int confidence,
        CancellationToken ct)
    {
        pos = Helper.SqlRepository.NormalizePosOrEmpty(pos);
        confidence = Helper.SqlRepository.NormalizeConfidence(confidence);

        if (entryId <= 0)
            return 0;

        if (string.IsNullOrWhiteSpace(pos) || pos == "unk")
            return 0;

        try
        {
            return await _sp.ExecuteAsync(
                "sp_DictionaryEntryPartOfSpeech_UpdateIfUnknown",
                new
                {
                    EntryId = entryId,
                    Pos = pos,
                    Confidence = confidence
                },
                ct,
                timeoutSeconds: 30);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to update POS | EntryId={EntryId} | Pos={Pos} | Confidence={Confidence}",
                entryId, pos, confidence);

            return 0;
        }
    }

    public async Task<int> BackfillConfidenceAsync(
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        try
        {
            return await _sp.ExecuteAsync(
                "sp_DictionaryEntryPartOfSpeech_BackfillConfidence",
                new { SourceCode = sourceCode },
                ct,
                timeoutSeconds: 60);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to backfill POS confidence | SourceCode={SourceCode}",
                sourceCode);

            return 0;
        }
    }

    private sealed class EntryNeedingPosRow
    {
        public long EntryId { get; set; }
        public string? Definition { get; set; }
    }
}