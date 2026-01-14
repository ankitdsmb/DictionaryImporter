namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryCrossReferenceResolvedWriter(string cs)
{
    public async Task WriteAsync(
        long sourceParsedId,
        long targetCanonicalWordId,
        string referenceType,
        CancellationToken ct)
    {
        const string sql = """
                           INSERT INTO dbo.DictionaryCrossReferenceResolved
                           (SourceParsedId, TargetCanonicalWordId, ReferenceType)
                           SELECT @SourceParsedId, @TargetCanonicalWordId, @ReferenceType
                           WHERE NOT EXISTS
                           (
                               SELECT 1
                               FROM dbo.DictionaryCrossReferenceResolved
                               WHERE SourceParsedId = @SourceParsedId
                                 AND TargetCanonicalWordId = @TargetCanonicalWordId
                           );
                           """;

        await using var conn = new SqlConnection(cs);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    SourceParsedId = sourceParsedId,
                    TargetCanonicalWordId = targetCanonicalWordId,
                    ReferenceType = referenceType
                },
                cancellationToken: ct));
    }
}