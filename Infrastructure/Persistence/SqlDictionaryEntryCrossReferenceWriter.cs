namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryCrossReferenceWriter(
        string connectionString,
        ILogger<SqlDictionaryEntryCrossReferenceWriter> logger)
    {
        public async Task WriteAsync(
            long parsedDefinitionId,
            CrossReference crossRef,
            CancellationToken ct)
        {
            const string sql = """
                               INSERT INTO dbo.DictionaryEntryCrossReference
                               (
                                   SourceParsedId,
                                   TargetWord,
                                   ReferenceType
                               )
                               SELECT
                                   @ParsedId,
                                   @Target,
                                   @Type
                               WHERE NOT EXISTS
                               (
                                   SELECT 1
                                   FROM dbo.DictionaryEntryCrossReference x
                                   WHERE x.SourceParsedId = @ParsedId
                                     AND x.TargetWord = @Target
                                     AND x.ReferenceType = @Type
                               );
                               """;

            var target =
                crossRef.TargetWord
                    .Trim()
                    .ToLowerInvariant();

            var type =
                crossRef.ReferenceType
                    .Trim();

            await using var conn =
                new SqlConnection(connectionString);

            await conn.OpenAsync(ct);

            var rows =
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            ParsedId = parsedDefinitionId,
                            Target = target,
                            Type = type
                        },
                        cancellationToken: ct));

            if (rows > 0)
                logger.LogDebug(
                    "CrossReference inserted | ParsedId={ParsedId} | Type={Type} | Target={Target}",
                    parsedDefinitionId,
                    type,
                    target);
        }
    }
}