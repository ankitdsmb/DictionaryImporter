using Dapper;
using DictionaryImporter.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlParsedDefinitionWriter
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlParsedDefinitionWriter> _logger;
        public SqlParsedDefinitionWriter(
            string connectionString,
            ILogger<SqlParsedDefinitionWriter> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }
        public async Task<long> WriteAsync(
            long dictionaryEntryId,
            ParsedDefinition parsed,
            long? parentParsedId,
            CancellationToken ct)
        {
            // ==================================================
            // NORMALIZATION (SCHEMA INVARIANTS)
            // ==================================================
            var meaningTitle =
                string.IsNullOrWhiteSpace(parsed.MeaningTitle)
                    ? string.Empty
                    : parsed.MeaningTitle.Trim();

            var definition =
                string.IsNullOrWhiteSpace(parsed.Definition)
                    ? string.Empty
                    : parsed.Definition.Trim();

            const string sql = """
            MERGE dbo.DictionaryEntryParsed AS target
            USING
            (
                SELECT
                    @DictionaryEntryId AS DictionaryEntryId,
                    ISNULL(@ParentParsedId, -1) AS ParentParsedId,
                    ISNULL(@MeaningTitle, '') AS MeaningTitle,
                    ISNULL(@SenseNumber, -1) AS SenseNumber
            ) AS source
            ON target.DictionaryEntryId = source.DictionaryEntryId
               AND ISNULL(target.ParentParsedId, -1) = source.ParentParsedId
               AND ISNULL(target.MeaningTitle, '') = source.MeaningTitle
               AND ISNULL(target.SenseNumber, -1) = source.SenseNumber

            WHEN MATCHED THEN
                UPDATE SET
                    Definition = target.Definition   -- NO-OP UPDATE (forces OUTPUT)

            WHEN NOT MATCHED THEN
                INSERT
                (
                    DictionaryEntryId,
                    ParentParsedId,
                    MeaningTitle,
                    SenseNumber,
                    DomainCode,
                    UsageLabel,
                    Definition,
                    RawFragment,
                    CreatedUtc
                )
                VALUES
                (
                    @DictionaryEntryId,
                    @ParentParsedId,
                    @MeaningTitle,
                    @SenseNumber,
                    @DomainCode,
                    @UsageLabel,
                    @Definition,
                    @RawFragment,
                    SYSUTCDATETIME()
                )

            OUTPUT
                inserted.DictionaryEntryParsedId;
            """;
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            long? parsedId;

            try
            {
                parsedId =
                await conn.ExecuteScalarAsync<long?>(
                    new CommandDefinition(
            sql,
            new
            {
                DictionaryEntryId = dictionaryEntryId,
                ParentParsedId = parentParsedId,
                MeaningTitle = meaningTitle,
                SenseNumber = parsed.SenseNumber,
                DomainCode = parsed.Domain,
                UsageLabel = parsed.UsageLabel,
                Definition = definition,
                RawFragment = parsed.RawFragment
            },
            cancellationToken: ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ParsedDefinition MERGE SQL failed | EntryId={EntryId} | ParentParsedId={ParentId} | Title={Title} | Sense={Sense}",
                    dictionaryEntryId,
                    parentParsedId,
                    meaningTitle,
                    parsed.SenseNumber);

                throw;
            }

            // ==================================================
            // HARD ASSERT (NO SILENT FAILURES)
            // ==================================================
            if (!parsedId.HasValue || parsedId <= 0)
            {
                _logger.LogError(
                    "ParsedDefinition MERGE affected 0 rows | EntryId={EntryId} | ParentParsedId={ParentId} | Title={Title} | Sense={Sense} | Definition={Definition}",
                    dictionaryEntryId,
                    parentParsedId,
                    meaningTitle,
                    parsed.SenseNumber,
                    definition);

                throw new InvalidOperationException(
                    $"Failed to MERGE ParsedDefinition for DictionaryEntryId={dictionaryEntryId}");
            }

            _logger.LogDebug(
                "ParsedDefinition written | EntryId={EntryId} | ParsedId={ParsedId} | ParentParsedId={ParentId} | Title={Title} | Sense={Sense}",
                dictionaryEntryId,
                parsedId.Value,
                parentParsedId,
                meaningTitle,
                parsed.SenseNumber);
            return parsedId.Value;
        }
    }
}