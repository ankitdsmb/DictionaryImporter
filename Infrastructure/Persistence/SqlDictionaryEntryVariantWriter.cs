namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryVariantWriter(string cs)
    {
        public async Task WriteAsync(
            long entryId,
            string variant,
            string type,
            CancellationToken ct)
        {
            const string sql = """
                               INSERT INTO dbo.DictionaryEntryVariant
                               (DictionaryEntryId, VariantText, VariantType)
                               SELECT @EntryId, @Variant, @Type
                               WHERE NOT EXISTS
                               (
                                   SELECT 1
                                   FROM dbo.DictionaryEntryVariant
                                   WHERE DictionaryEntryId = @EntryId
                                     AND VariantText = @Variant
                               );
                               """;

            await using var conn = new SqlConnection(cs);

            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        EntryId = entryId,
                        Variant = variant,
                        Type = type
                    },
                    cancellationToken: ct));
        }
    }
}