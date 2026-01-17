namespace DictionaryImporter.Infrastructure.PostProcessing
{
    public sealed class DictionaryEntryPartOfSpeechResolver(
        string connectionString,
        IPartOfSpeechInfererV2 inferer,
        ILogger<DictionaryEntryPartOfSpeechResolver> logger)
    {
        public async Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct)
        {
            await using var conn =
                new SqlConnection(connectionString);

            await conn.OpenAsync(ct);

            const string selectSql = """
                                     WITH RankedDefinitions AS
                                     (
                                         SELECT
                                             e.DictionaryEntryId,
                                             p.Definition,
                                             ROW_NUMBER() OVER
                                             (
                                                 PARTITION BY e.DictionaryEntryId
                                                 ORDER BY
                                                     CASE
                                                         WHEN p.ParentParsedId IS NULL THEN 0
                                                         WHEN p.SenseNumber IS NOT NULL THEN 1
                                                         ELSE 2
                                                     END
                                             ) AS rn
                                         FROM dbo.DictionaryEntry e
                                         JOIN dbo.DictionaryEntryParsed p
                                             ON p.DictionaryEntryId = e.DictionaryEntryId
                                         WHERE e.SourceCode = @SourceCode
                                           AND (e.PartOfSpeech IS NULL OR e.PartOfSpeech = 'unk')
                                     )
                                     SELECT
                                         DictionaryEntryId,
                                         Definition
                                     FROM RankedDefinitions
                                     WHERE rn = 1;
                                     """;

            var rows =
                await conn.QueryAsync<(long Id, string Definition)>(
                    selectSql,
                    new { SourceCode = sourceCode });

            var updated = 0;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();

                var result =
                    inferer.InferWithConfidence(row.Definition);

                if (result.Pos == "unk")
                    continue;

                const string updateSql = """
                                         UPDATE dbo.DictionaryEntry
                                         SET
                                             PartOfSpeech = @Pos,
                                             PartOfSpeechConfidence = @Confidence
                                         WHERE DictionaryEntryId = @Id
                                           AND (PartOfSpeech IS NULL OR PartOfSpeech = 'unk');
                                         """;

                var affected =
                    await conn.ExecuteAsync(
                        updateSql,
                        new
                        {
                            row.Id,
                            result.Pos,
                            result.Confidence
                        });

                if (affected > 0)
                    updated++;
            }

            logger.LogInformation(
                "POS resolution completed | Source={SourceCode} | Updated={Count}",
                sourceCode,
                updated);
        }
    }
}