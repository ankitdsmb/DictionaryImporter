namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryExampleWriter(
        string connectionString,
        ILogger<SqlDictionaryEntryExampleWriter> logger)
        : IDictionaryEntryExampleWriter
    {
        public async Task WriteAsync(
            long parsedDefinitionId,
            string exampleText,
            string sourceCode,
            CancellationToken ct)
        {
            const string sql = """
                               INSERT INTO dbo.DictionaryEntryExample
                               (
                                   DictionaryEntryParsedId,
                                   ExampleText,
                                   Source,
                                   CreatedUtc
                               )
                               VALUES
                               (
                                   @ParsedId,
                                   @ExampleText,
                                   @SourceCode,
                                   SYSUTCDATETIME()
                               );
                               """;

            await using var conn = new SqlConnection(connectionString);

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        ParsedId = parsedDefinitionId,
                        ExampleText = exampleText,
                        SourceCode = sourceCode
                    },
                    cancellationToken: ct));

            if (rows > 0)
                logger.LogDebug(
                    "Example inserted | ParsedId={ParsedId} | Source={Source}",
                    parsedDefinitionId,
                    sourceCode);
        }
    }
}