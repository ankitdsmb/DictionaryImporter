using System.Data;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntrySynonymWriter(
    string connectionString,
    ILogger<SqlDictionaryEntrySynonymWriter> logger)
    : IDictionaryEntrySynonymWriter
{
    public async Task WriteAsync(
        DictionaryEntrySynonym synonym,
        CancellationToken ct)
    {
        const string sql = """
                           IF NOT EXISTS (
                               SELECT 1
                               FROM dbo.DictionaryEntrySynonym
                               WHERE DictionaryEntryParsedId = @DictionaryEntryParsedId
                                 AND SynonymText = @SynonymText
                           )
                           BEGIN
                               INSERT INTO dbo.DictionaryEntrySynonym
                               (
                                   DictionaryEntryParsedId,
                                   SynonymText,
                                   Source,
                                   CreatedUtc
                               )
                               VALUES
                               (
                                   @DictionaryEntryParsedId,
                                   @SynonymText,
                                   ISNULL(@Source, 'dictionary'),
                                   SYSUTCDATETIME()
                               );
                           END
                           """;

        await using var conn = new SqlConnection(connectionString);

        try
        {
            var rows = await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        synonym.DictionaryEntryParsedId,
                        synonym.SynonymText,
                        synonym.Source
                    },
                    cancellationToken: ct));

            if (rows > 0)
                logger.LogDebug(
                    "Synonym inserted | ParsedId={ParsedId} | Synonym={Synonym}",
                    synonym.DictionaryEntryParsedId,
                    synonym.SynonymText);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to insert synonym | ParsedId={ParsedId} | Synonym={Synonym}",
                synonym.DictionaryEntryParsedId,
                synonym.SynonymText);
            throw;
        }
    }

    public async Task BulkWriteAsync(
        IEnumerable<DictionaryEntrySynonym> synonyms,
        CancellationToken ct)
    {
        var synonymList = synonyms.ToList();
        if (synonymList.Count == 0)
            return;

        const string sql = """
                           INSERT INTO dbo.DictionaryEntrySynonym
                           (
                               DictionaryEntryParsedId,
                               SynonymText,
                               Source,
                               CreatedUtc
                           )
                           SELECT
                               s.DictionaryEntryParsedId,
                               s.SynonymText,
                               ISNULL(s.Source, 'dictionary'),
                               SYSUTCDATETIME()
                           FROM @Synonyms s
                           WHERE NOT EXISTS (
                               SELECT 1
                               FROM dbo.DictionaryEntrySynonym es
                               WHERE es.DictionaryEntryParsedId = s.DictionaryEntryParsedId
                                 AND es.SynonymText = s.SynonymText
                           );
                           """;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        using var transaction = await conn.BeginTransactionAsync(ct);

        try
        {
            var synonymTable = new DataTable();
            synonymTable.Columns.Add("DictionaryEntryParsedId", typeof(long));
            synonymTable.Columns.Add("SynonymText", typeof(string));
            synonymTable.Columns.Add("Source", typeof(string));

            foreach (var synonym in synonymList)
                synonymTable.Rows.Add(
                    synonym.DictionaryEntryParsedId,
                    synonym.SynonymText,
                    synonym.Source ?? "dictionary");

            var param = new
            {
                Synonyms = synonymTable.AsTableValuedParameter("dbo.DictionaryEntrySynonymType")
            };

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    param,
                    transaction,
                    cancellationToken: ct));

            await transaction.CommitAsync(ct);

            logger.LogInformation(
                "Bulk inserted {Count} synonyms | TotalRows={Rows}",
                synonymList.Count,
                rows);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);

            logger.LogError(
                ex,
                "Failed to bulk insert synonyms | Count={Count}",
                synonymList.Count);
            throw;
        }
    }

    public async Task WriteSynonymsForParsedDefinition(
        long parsedDefinitionId,
        IEnumerable<string> synonyms,
        string sourceCode,
        CancellationToken ct)
    {
        var synonymList = synonyms.ToList();
        if (synonymList.Count == 0)
            return;

        var synonymEntities = synonymList.Select(synonym => new DictionaryEntrySynonym
        {
            DictionaryEntryParsedId = parsedDefinitionId,
            SynonymText = synonym,
            Source = sourceCode,
            CreatedUtc = DateTime.UtcNow
        });

        if (synonymList.Count > 10)
            await BulkWriteAsync(synonymEntities, ct);
        else
            foreach (var synonym in synonymEntities)
                await WriteAsync(synonym, ct);
    }
}