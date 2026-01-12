namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryCrossReferenceWriter
{
    private readonly string _connectionString;
    private readonly ILogger<SqlDictionaryEntryCrossReferenceWriter> _logger;

    public SqlDictionaryEntryCrossReferenceWriter(
        string connectionString,
        ILogger<SqlDictionaryEntryCrossReferenceWriter> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

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
            new SqlConnection(_connectionString);

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
            _logger.LogDebug(
                "CrossReference inserted | ParsedId={ParsedId} | Type={Type} | Target={Target}",
                parsedDefinitionId,
                type,
                target);
    }
}